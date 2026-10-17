using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

public sealed class SsoRefreshPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshIssuanceAndOfflineAccessRemainDisabledForEveryClientType(bool confidential)
    {
        await using var host = await SsoProtocolTestHost.Create();
        const string secret = "isolated-refresh-policy-client-secret";
        if (confidential) await host.MakeConfidential(secret);
        var authentication = confidential ? new Dictionary<string,string> { ["client_secret"] = secret } : new();
        var exchanged = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host), authentication);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        Assert.False((await exchanged.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("refresh_token", out _));
        var refresh = new Dictionary<string,string>(authentication)
        {
            ["client_id"] = SsoProtocolTestHost.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = "not-issued"
        };
        var rejected = await host.Client.PostAsync("/connect/token", new FormUrlEncodedContent(refresh));
        Assert.False(rejected.IsSuccessStatusCode);
        Assert.DoesNotContain("access_token", await rejected.Content.ReadAsStringAsync());
        var offline = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["scope"] = "openid offline_access" }));
        if (offline.Headers.Location is { } redirect)
            Assert.False(QueryHelpers.ParseQuery(redirect.Query).ContainsKey("code"));
        Assert.False(offline.IsSuccessStatusCode);
        Assert.DoesNotContain("refresh_token", await host.Client.GetStringAsync("/.well-known/openid-configuration"));
    }
}
