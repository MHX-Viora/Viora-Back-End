using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Viora.Domain.Developers;
using Xunit;

public sealed class SsoAuthorizationTests
{
    [Theory]
    [InlineData("code_challenge_method", "plain", "invalid_request")]
    [InlineData("code_challenge", null, "invalid_request")]
    [InlineData("scope", "openid wallet", "invalid_scope")]
    [InlineData("nonce", null, "invalid_request")]
    [InlineData("response_type", "token", "unsupported_response_type")]
    public async Task LibraryValidationRejectsUnsafeProtocolRequests(string parameter, string? value, string error)
    {
        await using var host = await SsoProtocolTestHost.Create();
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        var response = await host.Client.GetAsync(host.AuthorizationUrl(new() { [parameter] = value }));
        if (response.Headers.Location != null) Assert.Equal(error, QueryHelpers.ParseQuery(response.Headers.Location.Query)["error"].ToString());
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("error:" + error, (await response.Content.ReadAsStringAsync()).Split('\n')[0].Trim());
        }
    }
    [Fact]
    public async Task InvalidClientAndRedirectAreNeverUsedAsAnErrorDestination()
    {
        await using var host = await SsoProtocolTestHost.Create();
        foreach (var invalid in new[] { new Dictionary<string,string?> { ["client_id"] = "unknown" }, new Dictionary<string,string?> { ["redirect_uri"] = "https://evil.example/cb" } })
        {
            var response = await host.Client.GetAsync(host.AuthorizationUrl(invalid));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(response.Headers.Location);
        }
    }

    [Fact]
    public async Task ExistingUserSessionSkipsPasswordButNeverBypassesConsent()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var response = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "none" }));
        Assert.Equal("login_required", QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString());
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        response = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "none" }));
        Assert.Equal("consent_required", QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString());
    }

    [Fact]
    public async Task OrdinaryUnauthenticatedAuthorizationStartsFirstPartyBootstrap()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var response = await host.Client.GetAsync(host.AuthorizationUrl());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.StartsWith("/sso/start?returnUrl=", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SuspensionAndInvalidScopeCannotProceedEvenWithAnActiveSession()
    {
        await using var host = await SsoProtocolTestHost.Create();
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        await host.ChangeAppStatus(MiniAppStatus.Suspended);
        var response = await host.Client.GetAsync(host.AuthorizationUrl());
        Assert.Equal("access_denied", QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString());
    }
}
