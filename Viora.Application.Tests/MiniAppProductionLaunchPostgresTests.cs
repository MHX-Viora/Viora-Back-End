using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionLaunchPostgresTests
{
    [Fact]
    public async Task AuthorityLocksPreventDomainRevocationFromCommittingInsideLaunchCriticalSection()
    {
        await using var pg = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(options)) await migrated.Database.MigrateAsync();
        await using var f = await MiniAppProductionLaunchTests.Published(pg.ConnectionString);
        await using var db = new AppDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await new MiniAppProductionPolicy(db, f.Clock, MiniAppProductionLaunchTests.Options()).LockAsync(f.App.Id, MiniAppProductionActor.Anonymous, default);
        await using var contender = new AppDbContext(options);
        await using (var blocked = await contender.Database.BeginTransactionAsync())
        {
            await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '300ms'");
            var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"MiniAppDomainVerifications\" SET \"ExpiresAt\"={f.Clock.Now.UtcDateTime} WHERE \"MiniAppId\"={f.App.Id}"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState); await blocked.RollbackAsync();
        }
        await tx.CommitAsync();
        await contender.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppDomainVerifications\" SET \"ExpiresAt\"={f.Clock.Now.UtcDateTime} WHERE \"MiniAppId\"={f.App.Id}");
        await Assert.ThrowsAsync<DeveloperAuthException>(() => new MiniAppProductionPolicy(contender, f.Clock, MiniAppProductionLaunchTests.Options()).ReadAsync(f.App.Id, default));
    }
    [Fact]
    public async Task ConcurrentLaunchesCannotExceedBindingQuotaOrLeaveOrphanAudit()
    {
        await using var pg = await IsolatedPostgres.Start();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(dbOptions)) await migrated.Database.MigrateAsync();
        await using var f = await MiniAppProductionLaunchTests.Published(pg.ConnectionString);
        var options = MiniAppProductionLaunchTests.Options(); options.ActiveSessionsPerBinding = 1;
        var provider = new EphemeralDataProtectionProvider(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, options);
        var browser = bindings.Prepare(null); var second = bindings.Prepare(browser.CookieCredential);
        var ready = 0; var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Launch(MiniAppHostPrepareDto prepared)
        {
            await using var db = new AppDbContext(dbOptions);
            var fetcher = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = async () =>
            { if (Interlocked.Increment(ref ready) == 2) barrier.SetResult(); await barrier.Task.WaitAsync(TimeSpan.FromSeconds(8)); } };
            var service = new MiniAppProductionLaunchService(db, f.Clock, options, new(db, f.Clock, options), new(db, f.Clock), bindings, fetcher, provider, new(db));
            try { await service.LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.BindingId, prepared.BrowserNonce), default); return true; }
            catch (DeveloperAuthException error) when (error.Status is 409 or 429) { return false; }
        }
        Assert.Single(await Task.WhenAll(Launch(browser.Public), Launch(second.Public)), x => x);
        Assert.Single(await f.Db.Set<MiniAppProductionHostSession>().ToArrayAsync());
        Assert.Single(await f.Db.Set<MiniAppProductionLaunchGrant>().ToArrayAsync());
        Assert.Single(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
    }
}
