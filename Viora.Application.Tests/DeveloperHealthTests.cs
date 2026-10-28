using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using viora_BE.Developer;
using Xunit;

public sealed class DeveloperHealthTests
{
    [Fact]
    public async Task HealthIsPublicAndDoesNotRequireDatabaseOrExposeConfiguration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        await using var app = builder.Build();
        app.UseAuthorization();
        app.MapDeveloperEndpoints();
        await app.StartAsync();
        using var response = await app.GetTestClient().GetAsync("/api/developer/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Count);
        Assert.Equal("ok", payload["status"]);
        Assert.Equal("ankt-developer", payload["service"]);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
