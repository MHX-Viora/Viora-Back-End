using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using System.Security.Cryptography.X509Certificates;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoSecurityRegressionTests
{
    [Fact]
    public async Task CodeIssuedWithOldEncryptionKeySurvivesPromotionOfNewKeys()
    {
        using var replacement = new SsoRegistrationTests.Certificates();
        await using var host = await SsoProtocolTestHost.Create();
        var code = await SsoTokenEndpointTests.Code(host);
        using var signing = new X509Certificate2(replacement.Config()["Sso:SigningCertificatePath"]!,
            SsoRegistrationTests.Certificates.Password, X509KeyStorageFlags.EphemeralKeySet);
        using var encryption = new X509Certificate2(replacement.Config()["Sso:EncryptionCertificatePath"]!,
            SsoRegistrationTests.Certificates.Password, X509KeyStorageFlags.EphemeralKeySet);
        await host.WithServices(services =>
        {
            // Test-only promotion after issuance, preserving the old decryption key.
            var options = services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
            options.SigningCredentials.Insert(0, new(new X509SecurityKey(signing), SecurityAlgorithms.RsaSha256));
            options.EncryptionCredentials.Insert(0, new(new X509SecurityKey(encryption), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
            return Task.FromResult(true);
        });
        var response = await SsoTokenEndpointTests.Exchange(host, code); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        var identity = new JsonWebToken(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal(signing.Thumbprint, identity.Kid);
        var jwks = new JsonWebKeySet(await host.Client.GetStringAsync("/.well-known/jwks.json"));
        Assert.Equal(2, jwks.Keys.Count);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredCodeAndAccessTokenAreRejectedWithoutTimingSleeps(bool accessToken)
    {
        var clock = new Clock(); await using var host = await SsoProtocolTestHost.Create(timeProvider: clock);
        var code = await SsoTokenEndpointTests.Code(host);
        string? access = null;
        if (accessToken)
        {
            var exchange = await SsoTokenEndpointTests.Exchange(host, code); Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            access = (await exchange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        }
        clock.Now += TimeSpan.FromMinutes(10);
        if (accessToken)
        {
            using var info = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); info.Headers.Authorization = new("Bearer", access);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(info)).StatusCode);
        }
        else
        {
            var response = await SsoTokenEndpointTests.Exchange(host, code); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task OverlapPublishesBothSigningKeysAndExchangesCodeWithValidatedIdentity()
    {
        using var previous = new SsoRegistrationTests.Certificates();
        await using var host = await SsoProtocolTestHost.Create(configureSso: config =>
        {
            foreach (var purpose in new[] { "Signing", "Encryption" })
            {
                config[$"Sso:Previous{purpose}Certificates:0:Path"] = previous.Config()[$"Sso:{purpose}CertificatePath"];
                config[$"Sso:Previous{purpose}Certificates:0:Password"] = SsoRegistrationTests.Certificates.Password;
            }
        });
        var jwks = await host.Client.GetStringAsync("/.well-known/jwks.json");
        var keys = new JsonWebKeySet(jwks); Assert.Equal(2, keys.Keys.Count);
        Assert.All(keys.Keys, key => Assert.False(key.HasPrivateKey));
        var exchange = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var result = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        var identity = await new JsonWebTokenHandler().ValidateTokenAsync(result.GetProperty("id_token").GetString(), new()
        {
            ValidIssuer = "https://issuer.example/", ValidAudience = SsoProtocolTestHost.ClientId,
            IssuerSigningKeys = keys.GetSigningKeys(), ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero
        });
        Assert.True(identity.IsValid, identity.Exception?.Message);
    }

    [Fact]
    public async Task AValidDifferentClientCannotRedeemAnotherClientsCode()
    {
        await using var host = await SsoProtocolTestHost.Create(); var code = await SsoTokenEndpointTests.Code(host);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>(); var first = await db.MiniApps.SingleAsync();
            var other = new MiniApp { DeveloperAccountId = first.DeveloperAccountId, CreateRequestId = Guid.NewGuid(),
                AppId = "app_other", Name = "Other valid app", Category = "utilities", SsoEnabled = true,
                WebsiteUrl = first.WebsiteUrl, Status = MiniAppStatus.Draft };
            db.MiniAppOAuthClients.Add(new() { MiniApp = other, ClientKey = "other-valid-client", ClientType = "Public", SandboxEnabled = true });
            db.MiniAppRedirectUris.Add(new() { MiniApp = other, RedirectUri = SsoProtocolTestHost.RedirectUri });
            db.MiniAppDomainVerifications.Add(new() { MiniApp = other, Hostname = "partner.example.com", VerificationFileName = "other-client.txt",
                Status = MiniAppDomainStatus.Verified, ExpiresAt = DateTime.UtcNow.AddDays(1) });
            await using var transaction = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync();
            await services.GetRequiredService<MiniAppOidcRegistry>().SynchronizeAsync(other.Id, default);
            await transaction.CommitAsync(); return true;
        });
        var response = await SsoTokenEndpointTests.Exchange(host, code, new() { ["client_id"] = "other-valid-client" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("UnverifiedDomain")]
    public async Task EnvironmentAndDomainChangesBetweenCodeAndExchangeFailClosed(string change)
    {
        await using var host = await SsoProtocolTestHost.Create(); var code = await SsoTokenEndpointTests.Code(host);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            if (change == "Production") (await db.MiniAppOAuthClients.SingleAsync()).Environment = MiniAppOAuthEnvironment.Production;
            else (await db.MiniAppDomainVerifications.SingleAsync()).Status = MiniAppDomainStatus.Pending;
            await db.SaveChangesAsync(); return true;
        });
        var response = await SsoTokenEndpointTests.Exchange(host, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UserInfoRejectsMissingSocialAndUrlTransportCredentials()
    {
        await using var host = await SsoProtocolTestHost.Create();
        foreach (var path in new[] { "/connect/userinfo", "/connect/userinfo?access_token=forged" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(path)).StatusCode);
        using var social = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        social.Headers.Authorization = new("Bearer", host.SocialAccessToken());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(social)).StatusCode);
    }
}
