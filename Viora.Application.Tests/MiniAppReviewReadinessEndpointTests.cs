using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewReadinessEndpointTests
{
    [Fact]
    public async Task ReviewReadinessIsOwnedNoStoreStrictAndFailClosedWhenSandboxIsDisabled()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("review-readiness@example.test"));
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Readiness", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var revision = await host.WithServices(async services => await services.GetRequiredService<AppDbContext>().MiniApps.Select(x => x.Version).SingleAsync());
        var path = "/api/developer/mini-apps/" + app.AppId;
        var draft = (await (await http.PostAsJsonAsync(path + "/versions", new MiniAppVersionCreateRequest(Guid.NewGuid(), revision, "1.0"))).Content.ReadFromJsonAsync<MiniAppVersionSummaryDto>())!;
        var response = await http.GetAsync(path + "/review-readiness"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var result = (await response.Content.ReadFromJsonAsync<MiniAppReviewReadinessDto>())!;
        Assert.False(result.ReadyForReview); Assert.Equal(draft.Id, result.VersionId); Assert.NotEmpty(result.BlockingReasons);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + "/review-readiness?ready=true")).StatusCode);
        var submit = new MiniAppReviewSubmitRequest(Guid.NewGuid(), draft.Id, draft.ConcurrencyToken, result.AppVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path + "/reviews", submit)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path + "/reviews", new { submit.RequestId, submit.VersionId, submit.ConcurrencyToken, submit.AppVersion, status = "Approved" })).StatusCode);
        var history = await http.GetAsync(path + "/reviews?page=1&pageSize=20"); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Empty((await history.Content.ReadFromJsonAsync<MiniAppReviewPageDto>())!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + "/reviews?status=Approved")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path + "/reviews/" + Guid.NewGuid())).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("review-readiness-other@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path + "/review-readiness")).StatusCode);
        http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path + "/review-readiness")).StatusCode);
    }
}
