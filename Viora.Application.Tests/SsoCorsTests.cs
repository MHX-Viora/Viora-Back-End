using System.Net;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Persistence;

public sealed class SsoCorsTests
{
    [Theory]
    [InlineData("https://partner.example.com")]
    [InlineData("https://PARTNER.example.com/callback")]
    [InlineData("https://partner.example.com:443/callback")]
    public async Task CallbackSpellingDoesNotChangeItsNormalizedCorsOrigin(string callback)
    {
        await using var host = await SsoProtocolTestHost.Create();
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.MiniAppRedirectUris.SingleAsync()).RedirectUri = callback; await db.SaveChangesAsync(); return true;
        });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/connect/token");
        request.Headers.Add("Origin", "https://partner.example.com"); request.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await host.Client.SendAsync(request);
        Assert.Equal("https://partner.example.com", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/jwks.json")]
    public async Task PublicMetadataIsReadableWithoutCredentialedCors(string path)
    {
        await using var host = await SsoProtocolTestHost.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Add("Origin", "https://partner.example.com");
        var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://partner.example.com", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://partner.example.com.evil.example", false)]
    [InlineData("null", false)]
    [InlineData("https://firstparty.example", false)]
    public async Task PreflightOnlyAllowsVerifiedPublicCallbackOrigin(string origin, bool allowed)
    {
        await using var host = await SsoProtocolTestHost.Create();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/connect/token");
        request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed) Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task ExchangeRejectsBrowserOriginOutsideItsOwnClientRedirects()
    {
        await using var host = await SsoProtocolTestHost.Create(); var code = await SsoTokenEndpointTests.Code(host);
        host.Client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        var response = await SsoTokenEndpointTests.Exchange(host, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
    }
}
