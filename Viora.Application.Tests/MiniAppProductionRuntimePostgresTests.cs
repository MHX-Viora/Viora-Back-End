using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionRuntimePostgresTests
{
    [Fact]
    public async Task ConcurrentCloseIsIdempotentAndAnyRacingContextIsTerminalAfterward()
    {
        await using var pg = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(options)) await migrated.Database.MigrateAsync();
        await using var f = await ProductionRuntimeFixture.Create(pg.ConnectionString);
        async Task Close()
        {
            await using var db = new AppDbContext(options);
            await f.Service(db).CloseAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default);
        }
        async Task Context()
        {
            await using var db = new AppDbContext(options);
            try { await f.Service(db).ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default); }
            catch (DeveloperAuthException error) when (error.Status == 410) { }
        }
        await Task.WhenAll(Close(), Close(), Context());
        Assert.NotNull((await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).ClosedAt);
        Assert.Single(await f.F.Db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Closed).ToArrayAsync());
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service().ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default));
    }
    [Fact]
    public async Task RevocationPersistsInvalidationUnderPgGuardsWithoutTouchingUnrelatedSocialSession()
    {
        await using var pg = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(options)) await migrated.Database.MigrateAsync();
        await using var f = await ProductionRuntimeFixture.Create(pg.ConnectionString);
        var account = new Account { Email = "unrelated-runtime-fixture@example.test" };
        f.F.Db.AddRange(account, new RefreshToken { AccountId = account.Id, SessionId = Guid.NewGuid(),
            TokenHash = "unrelated-runtime-fixture", ExpiresAt = f.F.Clock.Now.UtcDateTime.AddHours(1) });
        await f.F.Db.SaveChangesAsync();
        await f.F.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.F.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service().ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default));
        var session = await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        Assert.NotNull(session.ClosedAt); Assert.Null(session.RuntimeCredentialHash);
        Assert.Single(await f.F.Db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Invalidated).ToArrayAsync());
        Assert.Null((await f.F.Db.RefreshTokens.SingleAsync(x => x.AccountId == account.Id)).RevokedAt);
        Assert.Single(await f.F.Db.DeveloperSessions.ToArrayAsync());
    }
}
