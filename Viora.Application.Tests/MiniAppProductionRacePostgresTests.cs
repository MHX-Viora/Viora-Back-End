using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionRacePostgresTests
{
    [Fact]
    public async Task RedeemAuthorityLocksSerializeSourceRevocationAndNeverLockUnrelatedSources()
    {
        await using var pg = await IsolatedPostgres.Start();
        var database = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(database)) await migrated.Database.MigrateAsync();
        await using var f = await MiniAppProductionLaunchTests.Published(pg.ConnectionString);
        var actors = await MiniAppProductionSecurityTests.Actors(f); var options = MiniAppProductionLaunchTests.Options();
        var provider = new EphemeralDataProtectionProvider(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, options);
        var browser = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, actors[0], new(Guid.NewGuid(), "Web", browser.Public.BindingId, browser.Public.BrowserNonce), default);
        await using var locked = new AppDbContext(database);
        await using var tx = await locked.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await new MiniAppProductionPolicy(locked, f.Clock, options).LockAsync(f.App.Id, actors[0], default);
        await using var contender = new AppDbContext(database);
        await using (var blocked = await contender.Database.BeginTransactionAsync())
        {
            await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '300ms'");
            // Different source is independent, even on the same account.
            await contender.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"RefreshTokens\" SET \"RevokedAt\"={DateTime.UtcNow} WHERE \"SessionId\"={actors[1].SourceSocialSessionId}");
            var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"RefreshTokens\" SET \"RevokedAt\"={DateTime.UtcNow} WHERE \"SessionId\"={actors[0].SourceSocialSessionId}"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState); await blocked.RollbackAsync();
        }
        await tx.CommitAsync();
        await contender.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"RefreshTokens\" SET \"RevokedAt\"={DateTime.UtcNow} WHERE \"SessionId\"={actors[0].SourceSocialSessionId}");
        f.Clock.Now = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<DeveloperAuthException>(() => MiniAppProductionGrantTests.Service(f, bindings)
            .RedeemAsync(receipt.Grant, browser.Public.BrowserNonce, browser.CookieCredential, default));
        Assert.Null((await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.Null((await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).RuntimeCredentialHash);
        Assert.Null((await f.Db.RefreshTokens.AsNoTracking().SingleAsync(item => item.SessionId == actors[1].SourceSocialSessionId)).RevokedAt);
    }
}
