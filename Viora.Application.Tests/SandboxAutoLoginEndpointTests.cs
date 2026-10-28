using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SandboxAutoLoginEndpointTests
{
    [Fact]
    public async Task OwnerEndpointsRejectAnonymousAndProductionBearerAndNeverSeedWhenDisabled()
    {
        await using var host = await MiniAppApiTests.Host.Create();
        var path = "/api/developer/mini-apps/app_" + new string('a', 43) + "/sandbox-test";
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync(path + "/sessions", new { })).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "production-token-is-not-a-developer-session");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(path)).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("auto-api@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync(path + "/sessions", new { userId = Guid.NewGuid() })).StatusCode);
        await host.WithServices(async s => {
            Assert.Equal(0, await s.GetRequiredService<AppDbContext>().Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
            return true;
        });
    }
}
