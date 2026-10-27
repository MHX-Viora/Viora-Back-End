using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionCatalogEndpointTests
{
    [Fact]
    public async Task StorageFailureReturnsSafeUnavailableWithoutDatabaseDetails()
    {
        await using var f = await SandboxFixture.Create();
        await using var app = await Start(f); using var http = app.GetTestClient();
        await f.Db.DisposeAsync();
        var response = await http.GetAsync("/api/mini-apps");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CATALOG_UNAVAILABLE", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("DbContext", body);
    }

    [Fact]
    public async Task AnonymousPublicRoutesReturnBoundedNoStoreDisplayDataAndRejectUnknownQueries()
    {
        await using var f = await SandboxFixture.Create();
        var published = await MiniAppProductionCatalogTests.AddUnitPublication(f, 'p');
        await using var app = await Start(f); using var http = app.GetTestClient();
        var response = await http.GetAsync("/api/mini-apps?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var page = (await response.Content.ReadFromJsonAsync<MiniAppPublicPageDto>())!;
        Assert.Equal(published.AppId, Assert.Single(page.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/mini-apps/" + published.AppId)).StatusCode);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=1&page=2", "?page=+1", "?pageSize=1.0", "?status=Published", "?url=https://example.com" })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/mini-apps" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/mini-apps/" + published.AppId + "?secret=true")).StatusCode);
        foreach (var id in new[] { "invalid", "app_" + new string('x', 43) })
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/mini-apps/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.PostAsJsonAsync("/api/mini-apps", new { status = "Published" })).StatusCode);
    }

    [Fact]
    public async Task DisabledModuleDoesNotExposeRoutesAndItsReadLimiterReturnsSafe429()
    {
        await using var f = await SandboxFixture.Create();
        await using (var disabled = await Start(f, enabled: false))
            Assert.Equal(HttpStatusCode.NotFound, (await disabled.GetTestClient().GetAsync("/api/mini-apps")).StatusCode);
        await using var limited = await Start(f, readLimit: 1); using var http = limited.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/mini-apps")).StatusCode);
        var response = await http.GetAsync("/api/mini-apps");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("RATE_LIMITED", await response.Content.ReadAsStringAsync());
    }

    private static async Task<WebApplication> Start(SandboxFixture f, bool enabled = true, int readLimit = 120)
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MiniAppAuth:Enabled"] = enabled.ToString(),
            ["MiniAppRuntime:Enabled"] = enabled.ToString(), ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com",
            ["MiniAppRuntime:HostOrigin"] = "https://host.example.com", ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com",
            ["MiniAppRuntime:ReadLimitPerMinute"] = readLimit.ToString()
        });
        builder.Services.AddSingleton<AppDbContext>(f.Db); builder.Services.AddSingleton<TimeProvider>(f.Clock);
        builder.Services.Configure<MiniAppReviewOptions>(o => o.Enabled = true);
        builder.Services.AddMiniAppProductionServices(builder.Configuration);
        var app = builder.Build(); app.UseRateLimiter(); app.MapMiniAppPublicApi(); await app.StartAsync(); return app;
    }
}
