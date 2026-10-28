using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Infrastructure.Persistence;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionGrantTests
{
    [Fact]
    public async Task FailureAfterCredentialCasRollsBackGrantRuntimeHashAndAuditTogether()
    {
        await using var f = await MiniAppProductionLaunchTests.Published(); var provider = new EphemeralDataProtectionProvider();
        var options = MiniAppProductionLaunchTests.Options(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, options);
        var prepared = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        f.Clock.Now = DateTimeOffset.UtcNow;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new FailRedeemedAudit()).Options);
        var service = new MiniAppProductionGrantService(db, f.Clock, options, new(db, f.Clock, options), new(db, f.Clock), bindings, new(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RedeemAsync(receipt.Grant, prepared.Public.BrowserNonce, prepared.CookieCredential, default));
        Assert.Null((await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).RuntimeCredentialHash);
        var grant = await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync();
        Assert.Null(grant.ConsumedAt); Assert.NotNull(grant.ReceiptProtected);
        Assert.Single(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
    }
    private sealed class FailRedeemedAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<MiniAppProductionRuntimeEvent>().Any(x => x.State == EntityState.Added && x.Entity.Code == MiniAppProductionEventCode.Redeemed))
                throw new InvalidOperationException("Fixture audit write failure.");
            return base.SavingChangesAsync(data, result, ct);
        }
    }
    [Fact]
    public async Task RedeemIssuesOnlyHashedRuntimeCredentialClearsReceiptAndRejectsReplay()
    {
        await using var f = await MiniAppProductionLaunchTests.Published();
        var provider = new EphemeralDataProtectionProvider(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options());
        var prepared = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.WebsiteUrl, "https://draft.example.com/changed"));
        f.Clock.Now = DateTimeOffset.UtcNow;
        var service = Service(f, bindings);
        var redeemed = await service.RedeemAsync(receipt.Grant, prepared.Public.BrowserNonce, prepared.CookieCredential, default);
        Assert.Equal("https://partner.example.com/app", redeemed.Public.WebsiteUrl);
        Assert.Equal("Production", redeemed.Public.Context.Environment); Assert.Equal("controlled-web", redeemed.Public.Context.Runtime);
        var session = await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        Assert.Equal(MiniAppProductionCredentials.Hash(redeemed.Credential), session.RuntimeCredentialHash);
        var grant = await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync();
        Assert.NotNull(grant.ConsumedAt); Assert.Null(grant.ReceiptProtected);
        Assert.DoesNotContain(redeemed.Credential, System.Text.Json.JsonSerializer.Serialize(redeemed.Public));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RedeemAsync(receipt.Grant, prepared.Public.BrowserNonce, prepared.CookieCredential, default));
        Assert.Equal(2, await f.Db.Set<MiniAppProductionRuntimeEvent>().CountAsync());
    }

    [Theory]
    [InlineData("browser")]
    [InlineData("nonce")]
    [InlineData("audience")]
    [InlineData("domain")]
    [InlineData("expiry")]
    public async Task InvalidRedeemCannotConsumeGrantOrIssueRuntimeCredential(string change)
    {
        await using var f = await MiniAppProductionLaunchTests.Published();
        var provider = new EphemeralDataProtectionProvider(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options());
        var prepared = bindings.Prepare(null); var other = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        if (change == "audience") await f.Db.Set<MiniAppProductionLaunchGrant>().ExecuteUpdateAsync(s => s.SetProperty(x => x.Audience, "https://other.example.com"));
        if (change == "domain") await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime));
        if (change == "expiry") f.Clock.Now += TimeSpan.FromSeconds(31);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f, bindings).RedeemAsync(receipt.Grant,
            change == "nonce" ? other.Public.BrowserNonce : prepared.Public.BrowserNonce,
            change == "browser" ? other.CookieCredential : prepared.CookieCredential, default));
        Assert.Null((await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).RuntimeCredentialHash);
        Assert.Null((await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.Single(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
    }
    internal static MiniAppProductionGrantService Service(SandboxFixture f, MiniAppProductionBindingService bindings) =>
        new(f.Db, f.Clock, MiniAppProductionLaunchTests.Options(), new(f.Db, f.Clock, MiniAppProductionLaunchTests.Options()),
            new(f.Db, f.Clock), bindings, new(f.Db));
}
