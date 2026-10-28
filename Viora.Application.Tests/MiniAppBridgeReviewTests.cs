using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;
using Xunit;

public sealed class MiniAppBridgeReviewTests
{
    private const string Verifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
    private static MiniAppBridgeService Bridge(SandboxFixture f) => new(f.Db,
        new(f.Db, Options.Create(new SandboxTestOptions { Enabled = true }), f.Clock),
        Options.Create(new MiniAppBridgeOptions { Enabled = true }), f.Clock);

    private static MiniAppReadinessService Readiness(SandboxFixture f)
    {
        var fetcher = new MiniAppReviewReadinessTests.Fetcher();
        var reader = new MiniAppSandboxEvidenceReader(f.Db, f.Service(), f.Options, f.Clock);
        return new(f.Db, new(f.Db, f.Clock), new(f.Db, new(f.Db, f.Clock), f.Clock),
            new(f.Db, new(f.Db, f.Clock, f.Options), fetcher, fetcher, f.Options, f.Clock, reader),
            reader, fetcher, f.Clock, new(f.Db, Bridge(f), f.Clock));
    }

    private static async Task<MiniAppVersionSummaryDto> Draft(SandboxFixture f)
    {
        f.App.LogoUrl = "https://partner.example.com/logo.png";
        f.App.ShortDescription = "Bridge application"; f.App.Description = "Reviewed bridge application";
        f.App.SupportEmail = "support@example.com"; f.App.PrivacyPolicyUrl = "https://partner.example.com/privacy";
        f.App.TermsOfServiceUrl = "https://partner.example.com/terms";
        await f.Db.SaveChangesAsync();
        var draft = await MiniAppVersionTests.Service(f).CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId,
            new(Guid.NewGuid(), f.App.Version, "1.0"), default);
        await f.Db.Entry(f.App).ReloadAsync();
        return draft;
    }

    private static async Task<MiniAppBridgeLaunch> Exchange(SandboxFixture f)
    {
        var bridge = Bridge(f); var origin = new Uri(f.App.WebsiteUrl!).GetLeftPart(UriPartial.Authority);
        var launch = await bridge.LaunchAsync(new(f.Actor.Id, f.Source.Id, true),
            new(f.App.AppId, MiniAppBridgeService.Challenge(Verifier), origin), default);
        await bridge.ExchangeAsync(new(f.App.AppId, launch.SessionId, launch.Code, Verifier), origin, default);
        return launch;
    }

    [Theory]
    [InlineData(null, true)] [InlineData("false", false)] [InlineData("true", true)]
    public void BridgeEnablesReviewUnlessExplicitlyDisabled(string? configured, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["MiniAppAuth:Enabled"] = "true", ["MiniAppReview:Enabled"] = configured }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddMiniAppReviewServices(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, provider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled);
    }

    [Fact]
    public async Task FreshBridgeExchangeReplacesLegacyExecutorAndIsRevalidated()
    {
        await using var f = await SandboxFixture.Create(); var draft = await Draft(f);
        var launch = await Exchange(f); var service = Readiness(f);
        var ticket = await service.PrepareAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default);
        Assert.True(ticket.Readiness.ReadyForReview); Assert.Equal(2, ticket.EvidenceSchemaVersion);
        Assert.Equal(launch.SessionId, ticket.Readiness.SandboxRunId);
        Assert.All(ticket.Readiness.RequiredChecks.Where(x => x.Key is "runtime" or "bridge" or "token" or "userInfo"),
            x => Assert.Equal("ServerProtocol", x.EvidenceSource));
        await service.RevalidateAsync(ticket, f.Actor.Id, f.Source.Id, f.App.AppId, default);
        await Bridge(f).RevokeAsync(launch.SessionId, new(f.Actor.Id, f.Source.Id, true), default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RevalidateAsync(ticket, f.Actor.Id, f.Source.Id, f.App.AppId, default));
    }

    [Theory]
    [InlineData("missing")] [InlineData("stale")] [InlineData("foreign-source")]
    [InlineData("changed")] [InlineData("revoked")] [InlineData("domain")]
    [InlineData("unexchanged")] [InlineData("production")] [InlineData("future")]
    public async Task InvalidBridgeEvidenceCannotSubmit(string invalid)
    {
        await using var f = await SandboxFixture.Create(); var draft = await Draft(f);
        if (invalid != "missing") await Exchange(f);
        if (invalid == "stale") f.Clock.Now += TimeSpan.FromMinutes(6);
        var changedId = Guid.NewGuid();
        if (invalid == "foreign-source") await f.Db.Set<MiniAppBridgeSession>().ExecuteUpdateAsync(s => s.SetProperty(x => x.SourceSessionId, changedId));
        if (invalid == "changed") await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, changedId));
        if (invalid == "revoked") await f.Db.Set<MiniAppBridgeSession>().ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        if (invalid == "domain") await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        if (invalid == "unexchanged") await f.Db.Set<MiniAppBridgeSession>().ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, (DateTime?)null).SetProperty(x => x.AccessHash, (string?)null));
        if (invalid == "production") await f.Db.Set<MiniAppBridgeSession>().ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSandbox, false));
        if (invalid == "future") { var future = f.Clock.Now.UtcDateTime.AddMinutes(1); await f.Db.Set<MiniAppBridgeSession>().ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, future)); }
        var service = Readiness(f);
        Assert.False((await service.PrepareAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default)).Readiness.ReadyForReview);
        var appVersion = await f.Db.MiniApps.Select(x => x.Version).SingleAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => new MiniAppReviewSubmissionService(f.Db, new(f.Db, f.Clock), service, f.Clock)
            .SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), draft.Id, draft.ConcurrencyToken, appVersion), default));
        Assert.Empty(await f.Db.MiniAppReviewRequests.ToArrayAsync());
    }

    [Fact]
    public async Task BridgeSubmitApprovePublishCatalogAndUnpublishUseTheSealedVersion()
    {
        await using var f = await SandboxFixture.Create(); var draft = await Draft(f); await Exchange(f);
        var readiness = Readiness(f);
        var submit = new MiniAppReviewSubmitRequest(Guid.NewGuid(), draft.Id, draft.ConcurrencyToken, f.App.Version);
        var submissions = new MiniAppReviewSubmissionService(f.Db, new(f.Db, f.Clock), readiness, f.Clock);
        var review = await submissions.SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, submit, default);
        Assert.Equal(review, await submissions.SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, submit, default));
        Assert.Empty(await f.Db.MiniAppPublications.ToArrayAsync());
        var admin = await MiniAppReviewAdminReadTests.Admin(f);
        var decisions = new MiniAppReviewDecisionService(f.Db, new(f.Db, f.Clock), f.Clock, readiness);
        review = await decisions.StartAsync(admin.Admin, admin.Session, review.Id, new(Guid.NewGuid(), review.ConcurrencyToken), default);
        f.Clock.Now += TimeSpan.FromMinutes(6); // Sealed evidence remains valid for admin decisions.
        review = await decisions.ApproveAsync(admin.Admin, admin.Session, review.Id, new(Guid.NewGuid(), review.ConcurrencyToken, null), default);
        Assert.Empty(await f.Db.MiniAppPublications.ToArrayAsync());
        var ready = (await readiness.PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default)).Readiness;
        var publishing = new MiniAppPublishingService(f.Db, new(f.Db, f.Clock), readiness, f.Clock,
            new(f.Db, bridgeOptions: Options.Create(new MiniAppBridgeOptions { Enabled = true })));
        var result = await publishing.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        var catalog = new MiniAppPublicCatalogService(f.Db, f.Clock, new(), bridgeEnabled: true);
        Assert.True((await catalog.DetailAsync(f.App.AppId, default)).CanOpen);
        var user = new Account { Email = "production-review@example.test", User = new User { DisplayName = "Real ANKT user" } };
        var userSource = Guid.NewGuid(); f.Db.Add(user);
        f.Db.RefreshTokens.Add(new() { Account = user, SessionId = userSource, TokenHash = "bridge-review-social", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) });
        await f.Db.SaveChangesAsync();
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Unreviewed changes"));
        Assert.NotEqual("Unreviewed changes", (await catalog.DetailAsync(f.App.AppId, default)).App.Name);
        var bridge = Bridge(f); var config = await bridge.ConfigAsync(f.App.AppId, null, default);
        var launch = await bridge.LaunchAsync(new(user.Id, userSource, false), new(f.App.AppId, MiniAppBridgeService.Challenge(Verifier), config.Origin), default);
        var token = await bridge.ExchangeAsync(new(f.App.AppId, launch.SessionId, launch.Code, Verifier), config.Origin, default);
        var profile = await bridge.MeAsync(token.AccessToken, config.Origin, default, f.App.AppId);
        Assert.False(profile.IsSandbox); Assert.Equal(user.User.Id, profile.Id);
        var active = (await readiness.PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default)).Readiness;
        await publishing.UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId,
            new(Guid.NewGuid(), result.AppVersion, null, result.Publication.Id, result.Publication.ConcurrencyToken, active.OperationalRevision), default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => catalog.DetailAsync(f.App.AppId, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => bridge.MeAsync(token.AccessToken, config.Origin, default, f.App.AppId));
    }
}
