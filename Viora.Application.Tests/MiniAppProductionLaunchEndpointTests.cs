using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
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

public sealed class MiniAppProductionLaunchEndpointTests
{
    [Fact]
    public async Task AnonymousLaunchUsesStrictBodyAndFirstPartyOriginAndInvalidBearerNeverDowngrades()
    {
        await using var f = await MiniAppProductionLaunchTests.Published();
        var provider = new EphemeralDataProtectionProvider();
        var prepared = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options()).Prepare(null);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MiniAppRuntime:Enabled"] = "true", ["MiniAppRuntime:HostOrigin"] = "https://host.example.com",
            ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com", ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com"
        });
        builder.Services.AddSingleton<IDataProtectionProvider>(provider); builder.Services.AddSingleton<AppDbContext>(f.Db);
        builder.Services.AddSingleton<TimeProvider>(f.Clock); builder.Services.AddSingleton<IPublicWebFetcher>(new MiniAppReviewReadinessTests.Fetcher());
        builder.Services.Configure<MiniAppReviewOptions>(o => o.Enabled = true);
        builder.Services.AddAuthentication("Bearer").AddJwtBearer();
        builder.Services.AddMiniAppProductionServices(builder.Configuration);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseRateLimiter(); app.MapMiniAppLaunchApi(); await app.StartAsync();
        using var http = app.GetTestClient(); http.BaseAddress = new("https://api.example.com");
        var path = "/api/mini-apps/" + f.App.AppId + "/launch";
        var command = new MiniAppLaunchRequest(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync(path, command)).StatusCode);
        http.DefaultRequestHeaders.Add("Origin", "https://app.example.com");
        http.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path, command)).StatusCode);
        http.DefaultRequestHeaders.Authorization = null;
        foreach (var body in new[] { "{}", "[]", "{\"requestId\":\"" + Guid.NewGuid() + "\",\"requestId\":\"" + Guid.NewGuid() + "\"}",
            "{\"url\":\"https://forged.example.com\"}", new string(' ', 7000) })
        {
            var response = await http.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        }
        var launched = await http.PostAsJsonAsync(path, command); Assert.Equal(HttpStatusCode.OK, launched.StatusCode);
        Assert.Equal("no-store", launched.Headers.CacheControl?.ToString());
        Assert.Equal("https://host.example.com/mini-app-host", (await launched.Content.ReadFromJsonAsync<MiniAppLaunchReceiptDto>())!.HostUrl);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path + "?environment=Sandbox", command)).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await http.PostAsJsonAsync(path, command with { Platform = "Android" })).StatusCode);
    }
}
