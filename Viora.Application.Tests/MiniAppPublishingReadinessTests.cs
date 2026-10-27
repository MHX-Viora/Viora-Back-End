using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppPublishingReadinessTests
{
    [Fact]
    public async Task EndpointRequiresOneExplicitVersionAndLiveOwnerAndStrictPublicationCommands()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("publication-readiness@example.test"));
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Publish", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var path = "/api/developer/mini-apps/" + app.AppId;
        var revision = await host.WithServices(async services => await services.GetRequiredService<AppDbContext>().MiniApps.Select(x => x.Version).SingleAsync());
        var draft = (await (await http.PostAsJsonAsync(path + "/versions", new MiniAppVersionCreateRequest(Guid.NewGuid(), revision, "1.0"))).Content.ReadFromJsonAsync<MiniAppVersionSummaryDto>())!;
        var response = await http.GetAsync(path + "/publication-readiness?versionId=" + draft.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False((await response.Content.ReadFromJsonAsync<MiniAppPublicationReadinessDto>())!.CanPublish);
        foreach (var query in new[] { "", "?versionId=bad", "?versionId=" + draft.Id + "&versionId=" + draft.Id, "?versionId=" + draft.Id + "&ready=true" })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + "/publication-readiness" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path + "/publish", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path + "/publish", new { requestId = Guid.NewGuid(), concurrencyToken = revision, versionId = draft.Id, status = "Published" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(path + "/publications?page=1&pageSize=20")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("publication-other@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path + "/publication-readiness?versionId=" + draft.Id)).StatusCode);
    }
    internal static async Task<MiniAppReviewSummaryDto> Approved(SandboxFixture f)
    {
        var started = await MiniAppReviewDecisionTests.Started(f);
        return await new MiniAppReviewDecisionService(f.Db, new(f.Db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f))
            .ApproveAsync(started.Admin, started.Session, started.Review.Id, new(Guid.NewGuid(), started.Review.ConcurrencyToken, null), default);
    }

    [Fact]
    public async Task ExplicitApprovedTargetRemainsReadyAfterNewDraftAndEvidenceExpiryWithoutProductionClient()
    {
        await using var f = await SandboxFixture.Create(); var approved = await Approved(f);
        var revision = Guid.NewGuid();
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Next draft").SetProperty(x => x.Version, revision));
        await MiniAppVersionTests.Service(f).CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), revision, "2.0"), default);
        f.Clock.Now += TimeSpan.FromMinutes(6);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        Assert.True(ready.CanPublish); Assert.False(ready.CanUnpublish); Assert.Equal(approved.Id, ready.ApprovedReviewRequestId);
        Assert.Equal(approved.VersionId, ready.VersionId); Assert.Empty(ready.BlockingReasons);
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
        Assert.False(await f.Db.MiniAppOAuthClients.AnyAsync(x => x.Environment == MiniAppOAuthEnvironment.Production));
    }

    [Fact]
    public async Task DraftIsNotApprovedAndUnknownVersionDoesNotLeak()
    {
        await using var f = await SandboxFixture.Create(); var draft = await MiniAppReviewReadinessTests.Ready(f);
        var service = MiniAppReviewReadinessTests.Service(f);
        var ready = (await service.PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default)).Readiness;
        Assert.False(ready.CanPublish); Assert.Contains("TARGET_NOT_APPROVED", ready.BlockingReasons);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PublicationAsync(Guid.NewGuid(), f.Source.Id, f.App.AppId, draft.Id, default));
    }

    [Theory]
    [InlineData("domain", "DOMAIN_UNVERIFIED")]
    [InlineData("suspended", "APP_SUSPENDED")]
    public async Task LiveSafetyBlocksAnApprovedTarget(string change, string reason)
    {
        await using var f = await SandboxFixture.Create(); var approved = await Approved(f);
        if (change == "domain") await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(-1)));
        else await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppStatus.Suspended));
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        Assert.False(ready.CanPublish); Assert.Contains(reason, ready.BlockingReasons);
    }

    [Fact]
    public async Task SafetyAndAppRevisionAreRecheckedAfterNetwork()
    {
        await using var f = await SandboxFixture.Create(); var approved = await Approved(f); var changed = false;
        var fetcher = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = async () =>
        { if (changed) return; changed = true; var revision = Guid.NewGuid(); await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, revision)); } };
        await Assert.ThrowsAsync<DeveloperAuthException>(() => MiniAppReviewReadinessTests.Service(f, fetcher)
            .PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default));
    }
}
