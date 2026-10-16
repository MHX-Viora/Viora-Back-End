using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoTokenEndpointTests
{
    [Fact]
    public async Task PostgreSqlConcurrentRedemptionHasExactlyOneWinner()
    {
        await using var postgres = await IsolatedPostgres.Start();
        await using var host = await SsoProtocolTestHost.Create(postgres);
        var code = await Code(host);
        var responses = await Task.WhenAll(Exchange(host, code), Exchange(host, code));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in responses) response.Dispose();
    }

    internal static async Task<string> Code(SsoProtocolTestHost host)
    {
        var form = await SsoConsentEndpointTests.Form(host);
        var response = await host.Client.PostAsync("/connect/authorize", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();
    }
    internal static Task<HttpResponseMessage> Exchange(SsoProtocolTestHost host, string code, Dictionary<string,string>? changes = null)
    {
        var form = new Dictionary<string,string> { ["grant_type"] = "authorization_code", ["client_id"] = SsoProtocolTestHost.ClientId,
            ["redirect_uri"] = SsoProtocolTestHost.RedirectUri, ["code"] = code, ["code_verifier"] = SsoProtocolTestHost.Verifier };
        if (changes != null) foreach (var pair in changes) form[pair.Key] = pair.Value;
        return host.Client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task RealExchangeProducesSignedBoundIdentityAndOpaqueUserInfoToken()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var code = await Code(host); var response = await Exchange(host, code);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(tokens.TryGetProperty("refresh_token", out _));
        var access = tokens.GetProperty("access_token").GetString()!;
        Assert.DoesNotContain('.', access);
        var jwks = new JsonWebKeySet(await host.Client.GetStringAsync("/.well-known/jwks.json"));
        var identity = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.GetProperty("id_token").GetString(), new TokenValidationParameters
        {
            ValidIssuer = "https://issuer.example/", ValidAudience = SsoProtocolTestHost.ClientId,
            IssuerSigningKeys = jwks.GetSigningKeys(), ValidateIssuerSigningKey = true, ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero
        });
        Assert.True(identity.IsValid, identity.Exception?.Message);
        Assert.Equal("protocol-test-nonce", identity.ClaimsIdentity.FindFirst("nonce")!.Value);
        var subject = identity.ClaimsIdentity.FindFirst("sub")!.Value; Assert.NotEqual(host.AccountId.ToString(), subject);
        Assert.DoesNotContain(identity.ClaimsIdentity.Claims, x => x.Type is "sid" or "social_session" or "sso_session");
        using var info = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); info.Headers.Authorization = new("Bearer", access);
        var profile = await host.Client.SendAsync(info); Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var json = await profile.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(subject, json.GetProperty("sub").GetString());
        Assert.False(json.TryGetProperty("email", out _));
        using var post = new HttpRequestMessage(HttpMethod.Post, "/connect/userinfo"); post.Headers.Authorization = new("Bearer", access);
        post.Content = new FormUrlEncodedContent(new Dictionary<string,string>());
        var posted = await host.Client.SendAsync(post); Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal(subject, (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sub").GetString());
        var replay = await Exchange(host, code); Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Theory]
    [InlineData("code_verifier", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("redirect_uri", "https://partner.example.com/other")]
    [InlineData("client_id", "unknown")]
    public async Task RedemptionRejectsWrongBinding(string key, string value)
    {
        await using var host = await SsoProtocolTestHost.Create(); var code = await Code(host);
        var response = await Exchange(host, code, new() { [key] = value });
        Assert.Equal(key == "client_id" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuspensionAfterCodeIssuanceBlocksExchange()
    {
        await using var host = await SsoProtocolTestHost.Create(); var code = await Code(host);
        await host.ChangeAppStatus(MiniAppStatus.Suspended);
        var response = await Exchange(host, code); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("consent")]
    [InlineData("domain")]
    [InlineData("account")]
    public async Task UserInfoImmediatelyRejectsRevokedAccess(string change)
    {
        await using var host = await SsoProtocolTestHost.Create();
        var response = await Exchange(host, await Code(host)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var access = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            switch (change)
            {
                case "suspended": (await db.MiniApps.SingleAsync()).Status = MiniAppStatus.Suspended; break;
                case "consent": (await db.MiniAppUserConsents.SingleAsync()).RevokedAt = DateTime.UtcNow; break;
                case "domain": (await db.MiniAppDomainVerifications.SingleAsync()).RevokedAt = DateTime.UtcNow; break;
                case "account": (await db.Accounts.SingleAsync()).DeletedAt = DateTime.UtcNow; break;
            }
            await db.SaveChangesAsync(); return true;
        });
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            using var request = new HttpRequestMessage(method, "/connect/userinfo"); request.Headers.Authorization = new("Bearer", access);
            if (method == HttpMethod.Post) request.Content = new FormUrlEncodedContent(new Dictionary<string,string>());
            var info = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, info.StatusCode);
            Assert.DoesNotContain("\"sub\"", await info.Content.ReadAsStringAsync());
        }
    }
}
