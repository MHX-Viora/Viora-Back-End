using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Viora.Domain.Developers;

public sealed class SsoTokenManagementTests
{
    [Fact]
    public async Task ReapprovalRevokesPersistedTokensFromThePreviousGrant()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var exchanged = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host));
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var previous = await db.MiniAppUserConsents.SingleAsync();
            await services.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default);
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            var count = 0;
            await foreach (var token in tokens.FindByAuthorizationIdAsync(previous.AuthorizationId!))
            {
                count++;
                Assert.Equal(OpenIddictConstants.Statuses.Revoked, await tokens.GetStatusAsync(token));
            }
            Assert.True(count > 0);
            return true;
        });
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("domain")]
    [InlineData("account")]
    [InlineData("owner-revoke")]
    public async Task IntrospectionImmediatelyReportsInactiveAfterLifecycleChanges(string change)
    {
        await using var host = await SsoProtocolTestHost.Create(allowIntrospection: true);
        const string secret = "isolated-confidential-test-secret-never-production";
        await host.MakeConfidential(secret);
        var exchanged = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host), new() { ["client_secret"] = secret });
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        var access = (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            switch (change)
            {
                case "suspended": (await db.MiniApps.SingleAsync()).Status = MiniAppStatus.Suspended; break;
                case "domain": (await db.MiniAppDomainVerifications.SingleAsync()).RevokedAt = DateTime.UtcNow; break;
                case "account": (await db.Accounts.SingleAsync()).DeletedAt = DateTime.UtcNow; break;
                case "owner-revoke":
                    await services.GetRequiredService<SsoAuthorizationOwnerService>().RevokeAsync(host.AccountId, (await db.MiniAppUserConsents.SingleAsync()).Id, default);
                    break;
            }
            await db.SaveChangesAsync(); return true;
        });
        var response = await host.Client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["client_id"] = SsoProtocolTestHost.ClientId, ["client_secret"] = secret, ["token"] = access }));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(!response.IsSuccessStatusCode || !json.GetProperty("active").GetBoolean());
        Assert.False(json.TryGetProperty("sub", out _));
    }
    [Fact]
    public async Task PublicClientCanRevokeItsAccessTokenWithoutRevokingConsent()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var exchange = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host));
        var access = (await exchange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        var revoked = await host.Client.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["client_id"] = SsoProtocolTestHost.ClientId, ["token"] = access, ["token_type_hint"] = "access_token" }));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var info = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); info.Headers.Authorization = new("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(info)).StatusCode);
        var again = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "none" }));
        Assert.True(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(again.Headers.Location!.Query).ContainsKey("code"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyExplicitlyAllowedConfidentialBackendCanIntrospectOwnToken(bool allowed)
    {
        await using var host = await SsoProtocolTestHost.Create(allowIntrospection: allowed);
        const string secret = "isolated-confidential-test-secret-never-production"; await host.MakeConfidential(secret);
        var exchanged = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host), new() { ["client_secret"] = secret });
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        var access = (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        var form = new Dictionary<string,string> { ["client_id"] = SsoProtocolTestHost.ClientId, ["client_secret"] = secret, ["token"] = access };
        var introspected = await host.Client.PostAsync("/connect/introspect", new FormUrlEncodedContent(form));
        if (!allowed) { Assert.False(introspected.IsSuccessStatusCode); return; }
        Assert.Equal(HttpStatusCode.OK, introspected.StatusCode);
        var json = await introspected.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(json.GetProperty("active").GetBoolean());
        Assert.Equal(SsoProtocolTestHost.ClientId, json.GetProperty("client_id").GetString());
        Assert.NotEqual(host.AccountId.ToString(), json.GetProperty("sub").GetString());
        host.Client.DefaultRequestHeaders.Add("Origin", "https://partner.example.com");
        Assert.False((await host.Client.PostAsync("/connect/introspect", new FormUrlEncodedContent(form))).IsSuccessStatusCode);
    }
}
