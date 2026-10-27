using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppVersionEndpointTests
{
    [Fact]
    public async Task VersionReadBudgetIsEnforcedPerDeveloper()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true, reviewReadLimit: 1); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-rate@example.test"));
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Rate", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var path = "/api/developer/mini-apps/" + app.AppId + "/versions";
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-rate-other@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path)).StatusCode); // Different actor has its own unused budget.
    }

    [Fact]
    public async Task OwnedVersionRoutesAreStrictBoundedAndNeverExposeCredentials()
    {
        await using var host = await MiniAppApiTests.Host.Create(review: true); var http = host.Client;
        var session = await host.Login("version-api@example.test"); http.DefaultRequestHeaders.Authorization = new("DeveloperSession", session);
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Versions", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var appVersion = await host.WithServices(async services => await services.GetRequiredService<AppDbContext>().MiniApps.Select(x => x.Version).SingleAsync());
        var path = $"/api/developer/mini-apps/{app.AppId}/versions";
        var create = new MiniAppVersionCreateRequest(Guid.NewGuid(), appVersion, "1.0");
        var response = await http.PostAsJsonAsync(path, create); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var draft = (await response.Content.ReadFromJsonAsync<MiniAppVersionSummaryDto>())!;
        Assert.Equal(draft, await (await http.PostAsJsonAsync(path, create)).Content.ReadFromJsonAsync<MiniAppVersionSummaryDto>());
        var detail = await http.GetAsync(path + "/" + draft.Id); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.DoesNotContain("secret", await detail.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(path + "?page=1&pageSize=1")).StatusCode);
        foreach (var query in new[] { "?environment=Production", "?page=1&page=2", "?pageSize=101", "?page=0", "?page=01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path, new { create.RequestId, create.AppVersion, create.VersionName, actor = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + "/" + draft.Id + "?snapshot=true")).StatusCode);
        using (var oversized = new StringContent(new string('x', 1025), System.Text.Encoding.UTF8, "application/json"))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PostAsync(path, oversized)).StatusCode);
        using (var duplicate = new StringContent("{\"versionName\":\"1\",\"VersionName\":\"2\"}", System.Text.Encoding.UTF8, "application/json"))
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync(path, duplicate)).StatusCode);
        var update = new MiniAppVersionUpdateRequest(Guid.NewGuid(), draft.ConcurrencyToken, "1.1");
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync(path + "/" + draft.Id,
            new { requestId = Guid.NewGuid(), concurrencyToken = draft.ConcurrencyToken, versionName = "Partial", ssoEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsJsonAsync(path + "/" + Guid.NewGuid(), update)).StatusCode);
        var updated = await http.PutAsJsonAsync(path + "/" + draft.Id, update); Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync(path + "/" + draft.Id, update with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(path + "/" + draft.Id + "/production-credentials", new { })).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-foreign@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path + "/" + draft.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsJsonAsync(path + "/" + draft.Id, update)).StatusCode);
        http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task ModuleIsDisabledByDefault()
    {
        await using var host = await MiniAppApiTests.Host.Create(); var http = host.Client;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("version-disabled@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/developer/mini-apps/app_disabled/versions")).StatusCode);
    }
}
