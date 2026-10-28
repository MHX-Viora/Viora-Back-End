using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Persistence;
using viora_BE.Operations;
using Xunit;

public sealed class PlatformReadinessTests
{
    [Fact]
    public async Task DatabaseProbeReturnsReadyOnlyForReachableOwnedDatabase()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var app = await Host(pg.ConnectionString);
        var response = await app.GetTestClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("{\"status\":\"ready\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnreachableDatabaseReturns503WithoutConnectionDetails()
    {
        await using var app = await Host("Host=127.0.0.1;Port=1;Database=stage10_unavailable;Username=stage10_private;Timeout=1;Pooling=false");
        var response = await app.GetTestClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("{\"status\":\"not_ready\"}", await response.Content.ReadAsStringAsync());
    }

    private static async Task<WebApplication> Host(string connection)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
        var app = builder.Build();
        app.MapPlatformReadiness();
        await app.StartAsync();
        return app;
    }
}
