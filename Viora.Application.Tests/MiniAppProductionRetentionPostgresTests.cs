using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionRetentionPostgresTests
{
    [Fact]
    public async Task PostgreSqlCleanupHonorsGuardsAndPreservesActiveRuntimeAndPublication()
    {
        await using var pg = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(options)) await migrated.Database.MigrateAsync();
        await using var f = await ProductionRuntimeFixture.Create(pg.ConnectionString);
        var historyId = Guid.NewGuid(); var historyBinding = Guid.NewGuid(); var correlation = Guid.NewGuid();
        var grantId = Guid.NewGuid(); var eventId = Guid.NewGuid(); var old = DateTime.UtcNow.AddDays(-8);
        var grantHash = MiniAppProductionCredentials.Hash(Guid.NewGuid().ToString());
        // Explicit expired retention dataset only, not actual historical launch/E2E
        // evidence. Current active runtime was created through real service workflow.
        await f.F.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MiniAppProductionHostSessions"
            SELECT (jsonb_populate_record(NULL::"MiniAppProductionHostSessions", to_jsonb(s) || jsonb_build_object(
                'Id', {historyId}, 'BindingId', {historyBinding}, 'RequestId', {Guid.NewGuid()}, 'CorrelationId', {correlation},
                'CreatedAt', {old}, 'ExpiresAt', {old.AddMinutes(15)}, 'LastActivityAt', {old}, 'RuntimeCredentialHash', NULL, 'ClosedAt', NULL))).*
            FROM "MiniAppProductionHostSessions" s WHERE s."Id"={f.SessionId};
            INSERT INTO "MiniAppProductionLaunchGrants"
            SELECT (jsonb_populate_record(NULL::"MiniAppProductionLaunchGrants", to_jsonb(g) || jsonb_build_object(
                'Id', {grantId}, 'HostSessionId', {historyId}, 'BindingId', {historyBinding}, 'GrantHash', {grantHash},
                'CreatedAt', {old}, 'ExpiresAt', {old.AddSeconds(30)}, 'ConsumedAt', NULL, 'ReceiptProtected', 'retention-fixture-protected'))).*
            FROM "MiniAppProductionLaunchGrants" g WHERE g."HostSessionId"={f.SessionId};
            INSERT INTO "MiniAppProductionRuntimeEvents"
            SELECT (jsonb_populate_record(NULL::"MiniAppProductionRuntimeEvents", to_jsonb(e) || jsonb_build_object(
                'Id', {eventId}, 'HostSessionId', {historyId}, 'CorrelationId', {correlation}, 'CreatedAt', {old}, 'Code', 5))).*
            FROM "MiniAppProductionRuntimeEvents" e WHERE e."HostSessionId"={f.SessionId} AND e."Code"=0;
            """);
        f.F.Clock.Now = DateTimeOffset.UtcNow;
        var result = await new MiniAppProductionRetentionService(f.F.Db, f.F.Clock, MiniAppProductionLaunchTests.Options(), new(f.F.Db)).CleanupAsync(default);
        Assert.Equal(1, result.EventsDeleted); Assert.Equal(1, result.GrantsExpired);
        Assert.Equal(1, result.GrantsDeleted); Assert.Equal(1, result.SessionsDeleted); Assert.Equal(0, result.SessionsClosed);
        Assert.Equal(f.SessionId, (await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).Id);
        Assert.Equal(f.F.App.AppId, (await f.Service().ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default)).AppId);
        Assert.Single(await f.F.Db.MiniAppPublications.ToArrayAsync()); Assert.Single(await f.F.Db.MiniAppReviewRequests.ToArrayAsync());
        Assert.False(await f.F.Db.RefreshTokens.AnyAsync(x => x.RevokedAt != null));
    }
}
