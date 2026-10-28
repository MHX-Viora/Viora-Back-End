using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionLaunchTests
{
    [Fact]
    public async Task LaunchPinsSealedTargetAndReplayReturnsOnlyItsBoundReceiptWithoutDuplicateAudit()
    {
        await using var f = await Published(); var options = Options(); var provider = new EphemeralDataProtectionProvider();
        var binding = new MiniAppProductionBindingService(provider, f.Clock, options); var prepared = binding.Prepare(null);
        var fetcher = new MiniAppReviewReadinessTests.Fetcher();
        var service = Service(f, provider, binding, fetcher);
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.WebsiteUrl, "https://draft.example.com/changed"));
        var command = new MiniAppLaunchRequest(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce);
        var receipt = await service.LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, command, default);
        Assert.Equal("https://host.example.com/mini-app-host", receipt.HostUrl);
        Assert.DoesNotContain(receipt.Grant, receipt.HostUrl); Assert.Equal(43, receipt.Grant.Length);
        var replay = await service.LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, command, default);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(receipt), System.Text.Json.JsonSerializer.Serialize(replay));
        var session = await f.Db.Set<MiniAppProductionHostSession>().SingleAsync();
        Assert.Null(session.AccountId); Assert.Null(session.SourceSocialSessionId);
        var grant = await f.Db.Set<MiniAppProductionLaunchGrant>().SingleAsync();
        Assert.NotEqual(receipt.Grant, grant.GrantHash); Assert.DoesNotContain(receipt.Grant, grant.ReceiptProtected!);
        Assert.Equal(MiniAppProductionCredentials.Hash(receipt.Grant), grant.GrantHash);
        Assert.Single(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
        var second = binding.Prepare(prepared.CookieCredential);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous,
            command with { BindingId = second.Public.BindingId, BrowserNonce = second.Public.BrowserNonce }, default));
    }

    [Fact]
    public async Task PreflightRunsOutsideTransactionAndSafetyRevocationDuringNetworkLeavesNoSessionOrAudit()
    {
        await using var f = await Published(); var provider = new EphemeralDataProtectionProvider();
        var binding = new MiniAppProductionBindingService(provider, f.Clock, Options()); var prepared = binding.Prepare(null);
        var fetcher = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = async () =>
        {
            Assert.Null(f.Db.Database.CurrentTransaction);
            await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime));
        } };
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f, provider, binding, fetcher).LaunchAsync(f.App.AppId,
            MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default));
        Assert.Empty(await f.Db.Set<MiniAppProductionHostSession>().ToArrayAsync());
        Assert.Empty(await f.Db.Set<MiniAppProductionLaunchGrant>().ToArrayAsync());
        Assert.Empty(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
    }

    internal static MiniAppProductionOptions Options() => new() { Enabled = true, HostOrigin = "https://host.example.com", FirstPartyOrigin = "https://app.example.com", ApiOrigin = "https://api.example.com" };
    internal static MiniAppProductionLaunchService Service(SandboxFixture f, IDataProtectionProvider provider, MiniAppProductionBindingService bindings, IPublicWebFetcher fetcher) =>
        new(f.Db, f.Clock, Options(), new(f.Db, f.Clock, Options()), new(f.Db, f.Clock), bindings, fetcher, provider, new(f.Db));
    internal static async Task<SandboxFixture> Published(string? connection = null)
    {
        var f = await SandboxFixture.Create(connection);
        f.App.AppId = "app_" + new string('p', 43); await f.Db.SaveChangesAsync();
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var readiness = MiniAppReviewReadinessTests.Service(f);
        var ready = (await readiness.PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await new MiniAppPublishingService(f.Db, new(f.Db, f.Clock), readiness, f.Clock,
            catalog: new(f.Db, Options())).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        return f;
    }
}
