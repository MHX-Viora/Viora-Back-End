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

public sealed class MiniAppProductionRatePolicyTests
{
    [Fact]
    public async Task ConfiguredModuleLimitsAreEnforcedIndependentlyWithoutChangingOtherRoutes()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseTestServer(); builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["MiniAppRuntime:Enabled"] = "true", ["MiniAppRuntime:FirstPartyOrigin"] = "https://app.example.com",
            ["MiniAppRuntime:HostOrigin"] = "https://host.example.com", ["MiniAppRuntime:ApiOrigin"] = "https://api.example.com",
            ["MiniAppRuntime:PrepareLimitPerMinute"] = "1", ["MiniAppRuntime:LaunchLimitPerMinute"] = "1",
            ["MiniAppRuntime:RedeemLimitPerMinute"] = "1", ["MiniAppRuntime:RuntimeLimitPerMinute"] = "1",
        });
        builder.Services.Configure<MiniAppReviewOptions>(options => options.Enabled = true);
        builder.Services.AddMiniAppProductionServices(builder.Configuration);
        await using var app = builder.Build(); app.UseRateLimiter();
        foreach (var name in new[] { "prepare", "launch", "redeem", "runtime" })
        {
            var policy = name == "launch" ? "mini-app-production-launch" : "mini-app-host-" + name;
            app.MapGet("/fixture/" + name, () => Microsoft.AspNetCore.Http.Results.NoContent()).RequireRateLimiting(policy);
        }
        app.MapGet("/unrelated", () => Microsoft.AspNetCore.Http.Results.NoContent());
        await app.StartAsync(); using var http = app.GetTestClient();
        foreach (var name in new[] { "prepare", "launch", "redeem", "runtime" })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync("/fixture/" + name)).StatusCode);
            var denied = await http.GetAsync("/fixture/" + name);
            Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
            Assert.Equal("no-store", denied.Headers.CacheControl?.ToString());
            Assert.Contains("RATE_LIMITED", await denied.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync("/unrelated")).StatusCode);
        }
    }

    [Theory]
    [InlineData(nameof(MiniAppProductionOptions.PrepareLimitPerMinute), 30)]
    [InlineData(nameof(MiniAppProductionOptions.LaunchLimitPerMinute), 20)]
    [InlineData(nameof(MiniAppProductionOptions.RedeemLimitPerMinute), 30)]
    [InlineData(nameof(MiniAppProductionOptions.RuntimeLimitPerMinute), 120)]
    [InlineData(nameof(MiniAppProductionOptions.EventsPerSessionPerMinute), 10)]
    public void RateConfigurationCanLowerButCannotDisableOrExceedTheReviewedCeiling(string property, int ceiling)
    {
        var options = MiniAppProductionLaunchTests.Options();
        var field = typeof(MiniAppProductionOptions).GetProperty(property)!;
        Assert.Equal(ceiling, field.GetValue(options));
        var validator = new MiniAppProductionOptionsValidator(new() { Enabled = true });
        foreach (var value in new[] { -1, 0, ceiling + 1, int.MaxValue })
        {
            field.SetValue(options, value); Assert.True(validator.Validate(null, options).Failed);
        }
        field.SetValue(options, 1); Assert.True(validator.Validate(null, options).Succeeded);
    }
}
