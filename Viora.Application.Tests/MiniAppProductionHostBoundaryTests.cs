using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Viora.Infrastructure.Developers;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionHostBoundaryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DedicatedHostnameCannotServePrivilegedRoutesIncludingDuringRollback(bool enabled)
    {
        await using var app = await Start(enabled); using var http = app.GetTestClient();
        foreach (var path in new[] { "/api/wallet", "/api/developer/mini-apps", "/api/mini-apps", "/sso/start", "/swagger/index.html", "/" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("https://host.example.com" + path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("https://api.example.com" + path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("https://host.example.com.:444" + path)).StatusCode);
        }
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            (await http.GetAsync("https://host.example.com/api/mini-app-host/prepare")).StatusCode);
        var nativeAsset = await http.GetAsync("https://host.example.com/mini-app-host/assets/native.mjs");
        Assert.True(nativeAsset.StatusCode == (enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound),
            "Native asset response: " + nativeAsset.StatusCode + "; body=" + await nativeAsset.Content.ReadAsStringAsync() +
            "; policy=" + string.Join(" ", nativeAsset.Headers.Select(h => h.Key)));
        foreach (var origin in new[] { "http://host.example.com", "https://host.example.com:444", "https://host.example.com." })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, origin + "/api/mini-app-host/prepare");
            request.Headers.Host = new Uri(origin).Authority;
            var response = await http.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Host check {origin}: {response.StatusCode}");
        }
    }
    [Fact]
    public async Task HostOnlyAllowlistStripsBearerAndRejectsUnrecognizedPaths()
    {
        await using var app = await Start(true); using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", "fixture-never-a-real-token");
        var response = await http.GetAsync("https://host.example.com/api/mini-app-host/prepare");
        Assert.Equal("anonymous-host", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("https://host.example.com/mini-app-host/arbitrary.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("https://host.example.com/api/mini-app-host/unknown")).StatusCode);
    }
    private static async Task<WebApplication> Start(bool enabled)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MiniAppRuntime:Enabled"] = enabled.ToString(), ["MiniAppRuntime:HostOrigin"] = "https://host.example.com",
            ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com", ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com"
        });
        builder.Services.Configure<MiniAppReviewOptions>(o => o.Enabled = true); builder.Services.AddMiniAppProductionServices(builder.Configuration);
        var app = builder.Build(); app.UseMiniAppProductionHostBoundary();
        app.MapGet("/mini-app-host/assets/native.mjs", () => "native module");
        foreach (var path in new[] { "/api/wallet", "/api/developer/mini-apps", "/api/mini-apps", "/sso/start", "/swagger/index.html", "/" })
            app.MapGet(path, () => "protected-route-sentinel");
        app.MapGet("/api/mini-app-host/prepare", (Microsoft.AspNetCore.Http.HttpContext context) => context.Request.Headers.Authorization.Count == 0 ? "anonymous-host" : "bearer-leaked");
        await app.StartAsync(); return app;
    }
}
