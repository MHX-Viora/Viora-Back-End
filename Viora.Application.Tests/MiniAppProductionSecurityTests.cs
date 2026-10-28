using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionSecurityTests
{
    [Theory]
    [InlineData("account")]
    [InlineData("source")]
    [InlineData("anonymous")]
    public async Task RequestReceiptCannotMoveAcrossAccountSourceOrAnonymousBoundary(string boundary)
    {
        await using var f = await MiniAppProductionLaunchTests.Published();
        var actors = await Actors(f); var options = MiniAppProductionLaunchTests.Options();
        var provider = new EphemeralDataProtectionProvider(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, options);
        var browser = bindings.Prepare(null); var request = new MiniAppLaunchRequest(Guid.NewGuid(), "Web", browser.Public.BindingId, browser.Public.BrowserNonce);
        var service = MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher());
        var original = await service.LaunchAsync(f.App.AppId, actors[0], request, default);
        var other = boundary == "account" ? actors[2] : boundary == "source" ? actors[1] : MiniAppProductionActor.Anonymous;
        Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.LaunchAsync(f.App.AppId, other, request, default))).Status);
        var replay = await service.LaunchAsync(f.App.AppId, actors[0], request, default);
        Assert.True(System.Text.Json.JsonSerializer.Serialize(original) == System.Text.Json.JsonSerializer.Serialize(replay), "Receipt replay changed.");
        Assert.Single(await f.Db.Set<MiniAppProductionHostSession>().ToArrayAsync());
        Assert.Single(await f.Db.Set<MiniAppProductionLaunchGrant>().ToArrayAsync());
    }

    [Fact]
    public async Task RevokedSourceBetweenLaunchAndRedeemCannotConsumeGrantOrIssueRuntime()
    {
        await using var f = await MiniAppProductionLaunchTests.Published(); var actors = await Actors(f);
        var provider = new EphemeralDataProtectionProvider(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options());
        var browser = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, actors[0], new(Guid.NewGuid(), "Web", browser.Public.BindingId, browser.Public.BrowserNonce), default);
        await f.Db.RefreshTokens.Where(token => token.SessionId == actors[0].SourceSocialSessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.RevokedAt, f.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => MiniAppProductionGrantTests.Service(f, bindings)
            .RedeemAsync(receipt.Grant, browser.Public.BrowserNonce, browser.CookieCredential, default));
        Assert.Null((await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.Null((await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).RuntimeCredentialHash);
        Assert.Empty(await f.Db.Set<MiniAppProductionRuntimeEvent>().Where(item => item.Code == MiniAppProductionEventCode.Redeemed).ToArrayAsync());
        Assert.Null((await f.Db.RefreshTokens.AsNoTracking().SingleAsync(token => token.SessionId == actors[1].SourceSocialSessionId)).RevokedAt);
    }

    [Fact]
    public async Task CrossBrowserCredentialSubstitutionCannotReadOrCloseAnotherLiveSession()
    {
        await using var f = await MiniAppProductionLaunchTests.Published(); var actors = await Actors(f);
        var options = MiniAppProductionLaunchTests.Options(); var provider = new EphemeralDataProtectionProvider();
        var bindings = new MiniAppProductionBindingService(provider, f.Clock, options);
        var first = bindings.Prepare(null); var second = bindings.Prepare(null);
        var launch = MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher());
        var a = await launch.LaunchAsync(f.App.AppId, actors[0], new(Guid.NewGuid(), "Web", first.Public.BindingId, first.Public.BrowserNonce), default);
        var b = await launch.LaunchAsync(f.App.AppId, actors[2], new(Guid.NewGuid(), "Web", second.Public.BindingId, second.Public.BrowserNonce), default);
        var grant = MiniAppProductionGrantTests.Service(f, bindings);
        f.Clock.Now = DateTimeOffset.UtcNow;
        var issuedA = await grant.RedeemAsync(a.Grant, first.Public.BrowserNonce, first.CookieCredential, default);
        f.Clock.Now = DateTimeOffset.UtcNow;
        var issuedB = await grant.RedeemAsync(b.Grant, second.Public.BrowserNonce, second.CookieCredential, default);
        var runtime = new MiniAppProductionRuntimeService(f.Db, f.Clock, options, new(f.Db, f.Clock, options), new(f.Db, f.Clock), bindings, new(f.Db));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => runtime.ContextAsync(a.SessionId, issuedB.Credential, first.Public.BrowserNonce, first.CookieCredential, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => runtime.CloseAsync(a.SessionId, issuedA.Credential, second.Public.BrowserNonce, second.CookieCredential, default));
        Assert.Equal(f.App.AppId, (await runtime.ContextAsync(a.SessionId, issuedA.Credential, first.Public.BrowserNonce, first.CookieCredential, default)).AppId);
        Assert.Equal(f.App.AppId, (await runtime.ContextAsync(b.SessionId, issuedB.Credential, second.Public.BrowserNonce, second.CookieCredential, default)).AppId);
        Assert.Equal(2, await f.Db.Set<MiniAppProductionHostSession>().CountAsync(item => item.ClosedAt == null));
    }

    internal static async Task<MiniAppProductionActor[]> Actors(SandboxFixture f)
    {
        // Domain-boundary fixtures, not browser/JWT or business E2E evidence.
        var a = new Account { Email = "source-a@fixture.test" }; var b = new Account { Email = "source-b@fixture.test" };
        var actors = new[] { new MiniAppProductionActor(a.Id, Guid.NewGuid()), new(a.Id, Guid.NewGuid()), new(b.Id, Guid.NewGuid()) };
        f.Db.AddRange(a, b);
        foreach (var actor in actors) f.Db.Add(new RefreshToken { AccountId = actor.AccountId!.Value, SessionId = actor.SourceSocialSessionId,
            TokenHash = "security-fixture-" + actor.SourceSocialSessionId, ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) });
        await f.Db.SaveChangesAsync(); return actors;
    }
}
