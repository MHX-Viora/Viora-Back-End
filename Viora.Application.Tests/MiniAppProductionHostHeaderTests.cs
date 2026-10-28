using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionHostHeaderTests
{
    [Fact]
    public async Task EmptyShellHasNoAppDataAndCanLoadOnlyTrustedEmbeddedAssets()
    {
        await using var f = await ProductionRuntimeFixture.Create(); await using var app = await Start(f); using var http = app.GetTestClient();
        http.BaseAddress = new("https://host.example.com");
        var response = await http.GetAsync("/mini-app-host"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-src 'none'", csp); Assert.Contains("frame-ancestors https://app.example.com", csp);
        Assert.Contains("script-src 'self'", csp); Assert.DoesNotContain("unsafe-inline", csp); Assert.DoesNotContain("*", csp);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(f.F.App.AppId, html); Assert.DoesNotContain("partner.example.com", html); Assert.DoesNotContain(f.Credential, html);
        Assert.Contains("/mini-app-host/assets/host.mjs", html);
        foreach (var asset in new[] { "host.mjs", "host.css", "bridge.mjs", "navigation.mjs", "native.mjs" })
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/mini-app-host/assets/" + asset)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/mini-app-host/assets/secrets.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("https://api.example.com/mini-app-host")).StatusCode);
    }
    [Fact]
    public async Task RunDocumentRequiresPrivateCookiesAndSelectsExactCspFromPinnedPublication()
    {
        await using var f = await ProductionRuntimeFixture.Create(); await using var app = await Start(f); using var http = app.GetTestClient();
        http.BaseAddress = new("https://host.example.com"); var path = "/mini-app-host/run/" + f.SessionId;
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Add("Cookie", MiniAppProductionHostApi.BindingCookie + "=" + f.Cookie + "; " + MiniAppProductionHostApi.RuntimeCookie(f.SessionId) + "=" + f.Credential);
        var response = await http.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-src https://partner.example.com;", csp); Assert.DoesNotContain("frame-src https:", csp.Replace("frame-src https://partner.example.com;", ""));
        Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
        var html = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(f.Credential, html); Assert.DoesNotContain(f.Nonce, html);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + "?grant=forged")).StatusCode);
    }
    private static async Task<WebApplication> Start(ProductionRuntimeFixture f)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders(); builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MiniAppRuntime:Enabled"] = "true", ["MiniAppRuntime:HostOrigin"] = "https://host.example.com",
            ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com", ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com"
        });
        builder.Services.AddSingleton<AppDbContext>(f.F.Db); builder.Services.AddSingleton<TimeProvider>(f.F.Clock); builder.Services.AddSingleton(f.Bindings);
        builder.Services.Configure<MiniAppReviewOptions>(o => o.Enabled = true); builder.Services.AddMiniAppProductionServices(builder.Configuration);
        var app = builder.Build(); app.UseRateLimiter(); app.MapMiniAppProductionHostDocuments(); await app.StartAsync(); return app;
    }
}
