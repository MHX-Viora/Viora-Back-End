using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionGrantPostgresTests
{
    [Fact]
    public async Task ConcurrentRedeemHasExactlyOneWinnerAndOneRuntimeCredentialAndAudit()
    {
        await using var pg = await IsolatedPostgres.Start();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using (var migrated = new AppDbContext(dbOptions)) await migrated.Database.MigrateAsync();
        await using var f = await MiniAppProductionLaunchTests.Published(pg.ConnectionString);
        var options = MiniAppProductionLaunchTests.Options(); var provider = new EphemeralDataProtectionProvider();
        var bindings = new MiniAppProductionBindingService(provider, f.Clock, options); var prepared = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        f.Clock.Now = DateTimeOffset.UtcNow;
        async Task<bool> Redeem()
        {
            await using var db = new AppDbContext(dbOptions);
            var service = new MiniAppProductionGrantService(db, f.Clock, options, new(db, f.Clock, options), new(db, f.Clock), bindings, new(db));
            try { await service.RedeemAsync(receipt.Grant, prepared.Public.BrowserNonce, prepared.CookieCredential, default); return true; }
            catch (DeveloperAuthException error) when (error.Status is 403 or 409 or 410) { return false; }
        }
        Assert.Single(await Task.WhenAll(Redeem(), Redeem()), x => x);
        var session = await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        Assert.Equal(64, session.RuntimeCredentialHash!.Length);
        var grant = await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync();
        Assert.NotNull(grant.ConsumedAt); Assert.Null(grant.ReceiptProtected);
        Assert.Single(await f.Db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Redeemed).ToArrayAsync());
    }
}
