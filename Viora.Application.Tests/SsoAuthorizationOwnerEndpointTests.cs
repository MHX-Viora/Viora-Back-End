using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Sso;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoAuthorizationOwnerEndpointTests
{
    [Fact]
    public async Task SocialUserListsAndRevokesOwnGrantWhileCookieAndDeveloperTokensCannot()
    {
        await using var host = await SsoProtocolTestHost.Create();
        await host.WithServices(services => services.GetRequiredService<SsoConsentService>()
            .GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/me/miniapp-authorizations")).StatusCode);
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/me/miniapp-authorizations")).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", host.SocialAccessToken(tokenType: "developer_access"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/me/miniapp-authorizations")).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", host.SocialAccessToken());
        var rows = await host.Client.GetFromJsonAsync<SsoAuthorizationDto[]>("/api/me/miniapp-authorizations"); var owned = Assert.Single(rows!);
        Assert.Empty((await host.Client.GetFromJsonAsync<SsoAuthorizationDto[]>("/api/me/miniapp-authorizations?offset=1"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/me/miniapp-authorizations?offset=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/me/miniapp-authorizations?offset=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/me/miniapp-authorizations/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync("/api/me/miniapp-authorizations/" + owned.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync("/api/me/miniapp-authorizations/" + owned.Id)).StatusCode);
        Assert.Null(await host.WithServices(s => s.GetRequiredService<SsoConsentService>().FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default)));
    }
}
