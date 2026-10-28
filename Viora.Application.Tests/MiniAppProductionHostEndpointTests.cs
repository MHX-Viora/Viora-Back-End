using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionHostEndpointTests
{
    [Fact]
    public async Task RedeemRequiresHostOriginAndBrowserCookieAndNeverReturnsRuntimeCredentialInJson()
    {
        await using var f = await MiniAppProductionLaunchTests.Published();
        var provider = new EphemeralDataProtectionProvider();
        var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options());
        var prepared = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        f.Clock.Now = DateTimeOffset.UtcNow;
        await using var app = await Start(fixture: f, provider: provider); using var http = app.GetTestClient();
        http.BaseAddress = new("https://host.example.com");
        var command = new MiniAppRedeemRequest(receipt.Grant, prepared.Public.BrowserNonce);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/mini-app-host/redeem", command)).StatusCode);
        http.DefaultRequestHeaders.Add("Origin", "https://host.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/mini-app-host/redeem", command)).StatusCode);
        http.DefaultRequestHeaders.Add("Cookie", MiniAppProductionHostApi.BindingCookie + "=" + prepared.CookieCredential + "; " + MiniAppProductionHostApi.BindingCookie + "=" + prepared.CookieCredential);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/mini-app-host/redeem", command)).StatusCode);
        http.DefaultRequestHeaders.Remove("Cookie");
        http.DefaultRequestHeaders.Add("Cookie", MiniAppProductionHostApi.BindingCookie + "=" + prepared.CookieCredential);
        var response = await http.PostAsJsonAsync("/api/mini-app-host/redeem", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-AnktMiniAppRuntime-" + receipt.SessionId.ToString("N") + "=", cookie);
        Assert.Contains("httponly", cookie); Assert.Contains("secure", cookie); Assert.Contains("samesite=none", cookie); Assert.DoesNotContain("domain=", cookie);
        var credential = cookie.Split(';')[0].Split('=', 2)[1]; Assert.Equal(43, credential.Length);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(credential, body); Assert.DoesNotContain(receipt.Grant, body);
        Assert.Equal("Production", (await response.Content.ReadFromJsonAsync<MiniAppProductionRedeemedDto>())!.Context.Environment);
        http.DefaultRequestHeaders.Remove("Cookie");
        http.DefaultRequestHeaders.Add("Cookie", MiniAppProductionHostApi.BindingCookie + "=" + prepared.CookieCredential + "; " + cookie.Split(';')[0]);
        http.DefaultRequestHeaders.Add("X-Ankt-Host-Session", receipt.SessionId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Ankt-Host-Nonce", prepared.Public.BrowserNonce);
        var context = await http.GetAsync("/api/mini-app-host/context");
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        Assert.Equal(f.App.AppId, (await context.Content.ReadFromJsonAsync<MiniAppProductionContextDto>())!.AppId);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/mini-app-host/heartbeat", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/mini-app-host/events", new { code = "BRIDGE_READY" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/mini-app-host/events", new { code = "REDEEMED" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/mini-app-host/events", new { code = "BRIDGE_READY", url = "https://untrusted.example.com/private" })).StatusCode);
        var external = await http.PostAsJsonAsync("/api/mini-app-host/external-link", new { });
        Assert.Equal(HttpStatusCode.OK, external.StatusCode);
        Assert.Equal("https://partner.example.com/app", (await external.Content.ReadFromJsonAsync<MiniAppProductionExternalLinkDto>())!.Url);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/mini-app-host/external-link", new { url = "https://forged.example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/mini-app-host/close", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/mini-app-host/close", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await http.GetAsync("/api/mini-app-host/context")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/mini-app-host/redeem", command)).StatusCode);
    }
    [Fact]
    public async Task PrepareRequiresExactHttpsHostAndReturnsOnlyNoStoreChallengeWithPrivateHostCookie()
    {
        await using var app = await Start(); using var http = app.GetTestClient();
        http.BaseAddress = new("https://host.example.com");
        foreach (var origin in new[] { "http://host.example.com", "https://api.example.com", "https://host.example.com.evil.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(origin + "/api/mini-app-host/prepare")).StatusCode);
        var response = await http.GetAsync("/api/mini-app-host/prepare");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<MiniAppHostPrepareDto>();
        Assert.NotNull(body); Assert.Equal(43, body.BrowserNonce.Length);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-AnktMiniAppBinding=", cookie);
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=none", cookie);
        Assert.DoesNotContain("domain=", cookie); Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("appId", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/mini-app-host/prepare?appId=forged")).StatusCode);
        http.DefaultRequestHeaders.Add("Origin", "https://partner.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/mini-app-host/prepare")).StatusCode);
    }
    [Fact]
    public async Task HostModuleDefaultsOff()
    {
        await using var app = await Start(false); using var http = app.GetTestClient(); http.BaseAddress = new("https://host.example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/mini-app-host/prepare")).StatusCode);
    }
    private static async Task<WebApplication> Start(bool enabled = true, SandboxFixture? fixture = null, IDataProtectionProvider? provider = null)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MiniAppRuntime:Enabled"] = enabled.ToString(), ["MiniAppRuntime:HostOrigin"] = "https://host.example.com",
            ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com", ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com"
        });
        builder.Services.AddSingleton<IDataProtectionProvider>(provider ?? new EphemeralDataProtectionProvider());
        if (fixture != null)
        {
            builder.Services.AddSingleton<Viora.Infrastructure.Persistence.AppDbContext>(fixture.Db);
            builder.Services.AddSingleton<TimeProvider>(fixture.Clock);
            builder.Services.AddSingleton<IPublicWebFetcher>(new MiniAppReviewReadinessTests.Fetcher());
        }
        builder.Services.Configure<MiniAppReviewOptions>(o => o.Enabled = true);
        builder.Services.AddMiniAppProductionServices(builder.Configuration);
        var app = builder.Build(); app.UseRateLimiter(); app.MapMiniAppProductionHostApi(); app.MapMiniAppProductionRuntimeApi(); await app.StartAsync(); return app;
    }
}
