using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.OpenApi;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Security;
using viora_BE.Controllers.OpenApi;
using viora_BE.OpenApi;
using Xunit;

public sealed class OpenApiOAuthTests
{
    [Fact] public async Task RevokedDeveloperStopsAutomaticLoginAndExistingPartnerTokens()
    {
        await using var f = await Fixture.Create(); var login = await f.Login();
        using (var scope = f.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Developers.ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeveloperStatus.Revoked));
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Prepare(f.Transaction)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
    }

    [Fact] public async Task AutoLoginNeverGrantsConsentAndAsksOnlyMissingScopes()
    {
        await using var f = await Fixture.Create();
        using (var scope = f.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().MiniAppUserConsents.ExecuteUpdateAsync(s => s.SetProperty(c => c.Granted, false));
        var pending = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("requiresConsent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("authorizeUrl").ValueKind);
        Assert.Equal(2, pending.GetProperty("permissions").GetArrayLength());
        using (var scope = f.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMiniAppService>().GrantConsentAsync(f.AccountId, f.AppId, new(["identity.login"], true), default);
        pending = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("profile.basic", Assert.Single(pending.GetProperty("permissions").EnumerateArray()).GetProperty("code").GetString());
        using (var scope = f.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMiniAppService>().GrantConsentAsync(f.AccountId, f.AppId, new(["profile.basic"], true), default);
        var ready = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(ready.GetProperty("requiresConsent").GetBoolean());
        Assert.Equal(0, ready.GetProperty("permissions").GetArrayLength());
        Assert.NotNull(ready.GetProperty("authorizeUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, (await f.Login()).Status);
    }

    [Fact] public async Task ExpiredHandoffAndClosedRuntimeBeforeTokenExchangeAreDenied()
    {
        await using var f = await Fixture.Create();
        var prepared = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        f.Clock.Advance(TimeSpan.FromSeconds(61));
        var expired = await f.Client.GetAsync(prepared.GetProperty("authorizeUrl").GetString());
        Assert.DoesNotContain("code=", expired.Headers.Location?.Query ?? "");
        LaunchMiniAppResponse launch;
        using (var scope = f.App.Services.CreateScope())
            launch = await scope.ServiceProvider.GetRequiredService<IMiniAppService>().LaunchAsync(f.AccountId, f.AppId, default);
        prepared = await (await f.Prepare(f.Transaction with { SessionId = launch.SessionId, SessionToken = launch.SessionToken })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await f.Client.GetAsync(prepared.GetProperty("authorizeUrl").GetString());
        var code = QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();
        using (var scope = f.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMiniAppService>().CloseSessionAsync(f.AccountId, f.AppId, new(launch.SessionId!.Value, launch.SessionToken!), default);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(code)).StatusCode);
    }

    [Fact] public async Task RuntimeLoginBindsSignedPartnerSessionAndRejectsChangedPublishedVersion()
    {
        await using var f = await Fixture.Create();
        LaunchMiniAppResponse launch;
        using (var scope = f.App.Services.CreateScope())
            launch = await scope.ServiceProvider.GetRequiredService<IMiniAppService>().LaunchAsync(f.AccountId, f.AppId, default);
        var input = f.Transaction with { SessionId = launch.SessionId, SessionToken = launch.SessionToken };
        var prepared = await f.Prepare(input);
        var data = await prepared.Content.ReadFromJsonAsync<JsonElement>();
        var authorized = await f.Client.GetAsync(data.GetProperty("authorizeUrl").GetString());
        var code = QueryHelpers.ParseQuery(authorized.Headers.Location!.Query)["code"].ToString();
        var exchanged = await f.Exchange(code);
        var tokens = await exchanged.Content.ReadFromJsonAsync<JsonElement>();
        var claims = new JsonWebTokenHandler().ReadJsonWebToken(tokens.GetProperty("id_token").GetString());
        Assert.Contains(claims.Claims, c => c.Type == "runtime_session_id" && c.Value == launch.SessionId.ToString());
        prepared = await f.Prepare(input);
        data = await prepared.Content.ReadFromJsonAsync<JsonElement>();
        using (var scope = f.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().MiniApps.ExecuteUpdateAsync(s => s.SetProperty(a => a.PublishedVersion, 2));
        authorized = await f.Client.GetAsync(data.GetProperty("authorizeUrl").GetString());
        Assert.DoesNotContain("code=", authorized.Headers.Location?.Query ?? "");
    }

    [Fact] public async Task RealCodeFlowIssuesScopedSeparateTokensAndRejectsBothCrossBoundaryDirections()
    {
        await using var f = await Fixture.Create();
        var login = await f.Login();
        Assert.Equal(HttpStatusCode.OK, login.Status);
        Assert.NotEmpty(login.AccessToken); Assert.NotEmpty(login.IdToken);
        var claims = new JsonWebTokenHandler().ReadJsonWebToken(login.AccessToken);
        Assert.Equal("https://openapi.example/", claims.Issuer);
        Assert.Contains("ankt-open-api", claims.Audiences);
        Assert.DoesNotContain(claims.Claims, c => c.Type == "role" || c.Value == f.AccountId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", f.MainToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/api/mini-apps/" + f.AppId + "/launch", login.AccessToken, true, "POST")).StatusCode);
        var keys = await f.Client.GetFromJsonAsync<JsonElement>("/oauth/jwks");
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(login.IdToken, new() { ValidIssuer = "https://openapi.example/", ValidAudience = "fixture-client", IssuerSigningKeys = new JsonWebKeySet(keys.GetRawText()).GetSigningKeys(), ValidateLifetime = true });
        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Contains(validation.ClaimsIdentity.Claims, c => c.Type == "nonce" && c.Value == f.Transaction.Nonce);
    }
    [Fact] public async Task CodeReplayWrongPkceWrongSecretAndWrongRedirectAreDenied()
    {
        await using var f = await Fixture.Create();
        var code = await f.Code();
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(code, verifier: new string('b', 43))).StatusCode);
        var fresh = await f.Code();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Exchange(fresh, secret: "wrong-secret")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(fresh, redirect: "https://partner.example/other")).StatusCode);
        var valid = await f.Code(); Assert.Equal(HttpStatusCode.OK, (await f.Exchange(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(valid)).StatusCode);
    }
    [Fact] public async Task MissingScopeAndOtherAppImpersonationAreForbidden()
    {
        await using var f = await Fixture.Create();
        var login = await f.Login(new[] { "openid" });
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Get("/v1/me/profile", login.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Get($"/v1/apps/{Guid.NewGuid()}/permissions", login.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Get($"/v1/apps/{f.AppId}/permissions", login.AccessToken)).StatusCode);
    }
    [Fact] public async Task ConsentRevocationAndSuspensionInvalidateExistingTokensAndNewIssuance()
    {
        await using var f = await Fixture.Create(); var login = await f.Login();
        using (var scope = f.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.MiniAppUserConsents.ExecuteUpdateAsync(s => s.SetProperty(c => c.Granted, false));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        using (var scope = f.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, MiniAppStatus.Suspended));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Prepare(f.Transaction)).StatusCode);
    }
    [Fact] public async Task StandardRevocationAndExpiredTokenAreRejected()
    {
        await using var f = await Fixture.Create(); var login = await f.Login();
        var revoke = await f.Client.PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = "fixture-client", ["client_secret"] = Fixture.Secret, ["token"] = login.AccessToken }));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        var expired = await f.Login();
        f.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", expired.AccessToken)).StatusCode);
    }
    [Fact] public async Task PublicHostBlocksInternalRoutesAndInternalHostBlocksProtocolRoutes()
    {
        await using var f = await Fixture.Create();
        foreach (var path in new[] { "/api/admin", "/api/wallet", "/api/messages", "/api/payments", "/swagger/v1/swagger.json" }) Assert.Equal(HttpStatusCode.NotFound, (await f.Get(path, f.MainToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Get("/oauth/jwks", null, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client.GetAsync("/.well-known/openid-configuration")).StatusCode);
        foreach (var path in new[] { "/V1/Me", "/OAuth/Token", "/OIDC/UserInfo", "/.WELL-KNOWN/openid-configuration" }) Assert.Equal(HttpStatusCode.NotFound, (await f.Get(path, null, true)).StatusCode);
    }

    [Fact] public async Task InactiveAccountIndependentAppAndSuspendedAppInvalidateCredentials()
    {
        await using var f = await Fixture.Create(); var login = await f.Login();
        using var scope = f.App.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, MiniAppStatus.Suspended));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Prepare(f.Transaction)).StatusCode);
        await db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, MiniAppStatus.Active).SetProperty(a => a.AuthenticationMode, MiniAppAuthenticationMode.Independent));
        var independent = await db.MiniApps.SingleAsync(); MiniAppTestPublication.Publish(independent, "identity.login", "profile.basic"); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Prepare(f.Transaction)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        await db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(a => a.AuthenticationMode, MiniAppAuthenticationMode.AnktSso));
        await db.Entry(independent).ReloadAsync(); MiniAppTestPublication.Publish(independent, "identity.login", "profile.basic"); await db.SaveChangesAsync();
        await db.Accounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Prepare(f.Transaction)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
    }

    [Fact] public async Task ForgedCallbackAndEscalatedScopesAreRejectedWithoutIssuingHandoff()
    {
        await using var f = await Fixture.Create();
        foreach (var transaction in new[] { f.Transaction with { RedirectUri = "https://evil.example/callback" }, f.Transaction with { State = "short" }, f.Transaction with { Nonce = "short" }, f.Transaction with { Scopes = new[] { "openid", "wallet" } } })
        {
            var response = await f.Prepare(transaction);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Prepare(f.Transaction with { Scopes = new[] { "openid", "email" } })).StatusCode);
        using var scope = f.App.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<OpenApiDbContext>().Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().CountAsync());
    }

    [Fact] public async Task HandoffIsBoundToTransactionSingleUseAndAdminRevocation()
    {
        await using var f = await Fixture.Create();
        var prepared = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        var url = prepared.GetProperty("authorizeUrl").GetString()!;
        var altered = await f.Client.GetAsync(QueryHelpers.AddQueryString(url, "state", "forged-state-abcdefghijklmnop"));
        Assert.False(altered.Headers.Location?.Query.Contains("code=") ?? false);
        var valid = await f.Client.GetAsync(url); Assert.Contains("code=", valid.Headers.Location!.Query);
        var replay = await f.Client.GetAsync(url); Assert.Contains("error=", replay.Headers.Location!.Query);
        var pending = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        using (var scope = f.App.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<OpenApiAccessService>().RevokeClient(f.AppId, default);
        var revoked = await f.Client.GetAsync(pending.GetProperty("authorizeUrl").GetString());
        Assert.Contains("error=", revoked.Headers.Location!.Query);
    }

    [Fact] public async Task CodeExpiresAndConsentRegrantDoesNotResurrectOldTokensOrCodes()
    {
        await using var f = await Fixture.Create(); var login = await f.Login(); var code = await f.Code();
        using (var scope = f.App.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IMiniAppService>();
            await runtime.RevokeConsentAsync(f.AccountId, f.AppId, default);
            await runtime.GrantConsentAsync(f.AccountId, f.AppId, new(new[] { "identity.login", "profile.basic" }, true), default);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(code)).StatusCode);
        var expiredCode = await f.Code(); f.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(expiredCode)).StatusCode);
    }

    [Fact] public async Task UserInfoMetadataAccountLinkIsolationAndPerUserRateLimitAreEnforced()
    {
        await using var f = await Fixture.Create(); var login = await f.Login();
        var metadata = await f.Client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        Assert.Equal("https://openapi.example/oidc/userinfo", metadata.GetProperty("userinfo_endpoint").GetString());
        Assert.Equal(HttpStatusCode.OK, (await f.Get("/oidc/userinfo", login.AccessToken)).StatusCode);
        var link = await f.Get("/v1/account-links", login.AccessToken, method: "POST");
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Get("/v1/account-links/" + Guid.NewGuid(), login.AccessToken, method: "DELETE")).StatusCode);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 61; i++) { last?.Dispose(); last = await f.Get("/v1/me", login.AccessToken); }
        Assert.Equal((HttpStatusCode)429, last!.StatusCode); last.Dispose();
    }

    [Fact] public async Task AdminRevokeInvalidatesAccessCodeAndPendingHandoffAndInternalFinancialChatRoutesRejectPartnerJwt()
    {
        await using var f = await Fixture.Create(); var login = await f.Login(); var code = await f.Code();
        var pending = await (await f.Prepare(f.Transaction)).Content.ReadFromJsonAsync<JsonElement>();
        foreach (var path in new[] { "/api/wallet", "/api/wallet/payments", "/api/chat/conversations", "/api/admin/mini-apps" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get(path, login.AccessToken, true)).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://internal.example/api/admin/mini-apps/{f.AppId}/open-api/revoke-tokens") { Content = JsonContent.Create(new { reason = "Synthetic security regression" }) };
        request.Headers.Authorization = new("Bearer", f.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, (await f.Client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Get("/v1/me", login.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Exchange(code)).StatusCode);
        Assert.Contains("error=", (await f.Client.GetAsync(pending.GetProperty("authorizeUrl").GetString())).Headers.Location!.Query);
    }

    [Fact] public async Task RegisteredSecondClientCannotExchangeFirstClientsCodeAndUntrustedOriginGetsNoCorsPermission()
    {
        await using var f = await Fixture.Create(); var code = await f.Code();
        using (var scope = f.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var first = await db.MiniApps.FirstAsync();
            var second = new MiniApp { DeveloperId = first.DeveloperId, Name = "Second", Slug = "second", ClientId = "fixture-second", ClientSecretHash = first.ClientSecretHash, Status = MiniAppStatus.Active, WebUrl = first.WebUrl, CallbackUrl = first.CallbackUrl, CallbackUrls = first.CallbackUrls, AllowedDomains = first.AllowedDomains, AllowedOrigins = first.AllowedOrigins, AuthenticationMode = MiniAppAuthenticationMode.AnktSso };
            MiniAppTestPublication.Publish(second, "identity.login", "profile.basic"); db.MiniApps.Add(second);
            await db.SaveChangesAsync();
        }
        var swapped = await f.Client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = "fixture-second", ["client_secret"] = Fixture.Secret, ["code"] = code, ["redirect_uri"] = f.Transaction.RedirectUri, ["code_verifier"] = f.Verifier }));
        Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        var login = await f.Login();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://openapi.example/v1/me"); request.Headers.Authorization = new("Bearer", login.AccessToken); request.Headers.Add("Origin", "https://evil.example");
        var response = await f.Client.SendAsync(request); Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact] public async Task DisabledFeatureKeepsInternalRouteButClosesConfiguredOpenApiHostAndMixedCaseProtocol()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        await using var app = builder.Build(); app.UsePartnerOpenApiBoundary(new() { Enabled = false, Issuer = "https://openapi.example/" });
        app.MapGet("/api/wallet", () => "synthetic internal resource"); app.MapGet("/api/open-api/authorize", () => "must be blocked");
        await app.StartAsync(); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("https://openapi.example/api/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("https://internal.example/api/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("https://internal.example/API/Open-Api/Authorize")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("https://internal.example/OAuth/Token")).StatusCode);
    }

    [Fact] public async Task PublicClientRequiresPkceWithoutASecretAndCannotUseAnotherClientsCode()
    {
        await using var f = await Fixture.Create();
        using (var scope = f.App.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var app = await db.MiniApps.SingleAsync(); app.ClientAuthenticationMethod = MiniAppClientAuthenticationMethod.None; MiniAppTestPublication.Publish(app, "identity.login", "profile.basic"); await db.SaveChangesAsync(); }
        var code = await f.Code();
        var body = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = "fixture-client", ["code"] = code, ["redirect_uri"] = f.Transaction.RedirectUri, ["code_verifier"] = f.Verifier };
        var response = await f.Client.PostAsync("/oauth/token", new FormUrlEncodedContent(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var wrong = await f.Code(); body["code"] = wrong; body["code_verifier"] = new string('b', 43);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Client.PostAsync("/oauth/token", new FormUrlEncodedContent(body))).StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string Secret = "synthetic-fixture-secret";
        private const string MainKey = "synthetic-internal-signing-key-for-tests-only-123456";
        public required WebApplication App; public required HttpClient Client; public required SqliteConnection RegistryConnection; public required SqliteConnection ProtocolConnection;
        public required Guid AccountId; public required Guid AppId; public required string MainToken;
        public required TestClock Clock;
        public string AdminToken => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("internal-fixture", "ankt-main", new[] { new Claim("sub", AccountId.ToString()), new Claim("role", "2"), new Claim("token_type", "access") }, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(MainKey)), SecurityAlgorithms.HmacSha256)));
        public string Verifier = new('a', 43);
        public OpenApiTransaction Transaction => new("fixture-client", "https://partner.example/callback", new string('s', 32), MiniAppSecurityPolicy.CreatePkceChallenge(Verifier), "S256", new string('n', 32), new[] { "openid", "profile" });
        public static async Task<Fixture> Create()
        {
            var registry = new SqliteConnection("Data Source=:memory:"); await registry.OpenAsync(); registry.CreateFunction("char_length", (string s) => s.Length);
            var protocol = new SqliteConnection("Data Source=:memory:"); await protocol.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            var clock = new TestClock(); builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["OpenApi:Enabled"] = "true", ["OpenApi:Issuer"] = "https://openapi.example/", ["OpenApi:Audience"] = "ankt-open-api", ["OpenApi:AuthorizationUi"] = "https://host.example/oauth/authorize", ["OpenApi:KnownProxies:0"] = "127.0.0.1" });
            builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(registry));
            builder.Services.AddScoped<IClientCredentialService, ClientCredentialService>(); builder.Services.AddScoped<IMiniAppService, MiniAppService>();
            builder.Services.AddControllers().AddApplicationPart(typeof(OpenApiHostController).Assembly);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims = false; o.TokenValidationParameters = new() { ValidIssuer = "internal-fixture", ValidAudience = "ankt-main", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(MainKey)), RoleClaimType = "role", ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 } }; });
            builder.Services.AddAuthorization();
            builder.Services.AddCors(o => o.AddPolicy("fixture", p => p.WithOrigins("https://host.example").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
            builder.Services.AddRateLimiter(o => o.AddPolicy("mini-app-launch", c => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
            var settings = builder.AddPartnerOpenApi(o => o.UseSqlite(protocol));
            var app = builder.Build(); app.UsePartnerOpenApiBoundary(settings);
            app.UseExceptionHandler(b => b.Run(async context => { var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error; context.Response.StatusCode = ex is MiniAppException mini ? mini.StatusCode : 500; await context.Response.WriteAsJsonAsync(new { error = ex?.Message }); }));
            app.UseRouting(); app.UseCors("fixture"); app.UseAuthentication(); app.UsePartnerOpenApiPrincipalLimits(settings); app.UseRateLimiter(); app.UseAuthorization(); app.MapControllers();
            var accountId = Guid.NewGuid(); var appId = Guid.NewGuid();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
                await scope.ServiceProvider.GetRequiredService<OpenApiDbContext>().Database.EnsureCreatedAsync();
                var developer = new Developer { Name = "Fixture developer", Email = "fixture@example.com", Status = DeveloperStatus.Active };
                var appEntity = new MiniApp { Id = appId, Developer = developer, Name = "Fixture app", Slug = "fixture-app", ClientId = "fixture-client", ClientSecretHash = new ClientCredentialService().HashSecret(Secret), Status = MiniAppStatus.Active, WebUrl = "https://partner.example/", CallbackUrl = "https://partner.example/callback", CallbackUrls = new[] { "https://partner.example/callback" }, AllowedDomains = new[] { "partner.example" }, AllowedOrigins = new[] { "https://partner.example" }, AuthenticationMode = MiniAppAuthenticationMode.AnktSso, ClientAuthenticationMethod = MiniAppClientAuthenticationMethod.ClientSecretPost };
                MiniAppTestPublication.Publish(appEntity, "identity.login", "profile.basic");
                db.AddRange(new Account { Id = accountId, Email = "person@example.com" }, new User { AccountId = accountId, DisplayName = "Fixture user" }, appEntity);
                var permissions = await db.MiniAppPermissions.Where(p => p.Code == "identity.login" || p.Code == "profile.basic").ToArrayAsync();
                foreach (var permission in permissions) { db.Add(new MiniAppPermissionMapping { MiniAppId = appId, PermissionId = permission.Id }); db.Add(new MiniAppUserConsent { AccountId = accountId, MiniAppId = appId, PermissionId = permission.Id, Granted = true }); }
                db.Add(new MiniAppVerifiedDomain { MiniAppId = appId, Host = "partner.example", ChallengeToken = "fixture", VerifiedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
            }
            await app.StartAsync(); var client = app.GetTestClient(); client.BaseAddress = new Uri("https://openapi.example");
            var jwt = new JwtSecurityToken("internal-fixture", "ankt-main", new[] { new Claim("sub", accountId.ToString()), new Claim("token_type", "access") }, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(MainKey)), SecurityAlgorithms.HmacSha256));
            return new() { App = app, Client = client, RegistryConnection = registry, ProtocolConnection = protocol, AccountId = accountId, AppId = appId, MainToken = new JwtSecurityTokenHandler().WriteToken(jwt), Clock = clock };
        }
        public async Task<HttpResponseMessage> Prepare(OpenApiTransaction transaction)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://internal.example/api/open-api/authorize") { Content = JsonContent.Create(transaction) }; req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MainToken); return await Client.SendAsync(req);
        }
        public async Task<string> Code(string[]? scopes = null)
        {
            var prep = await Prepare(Transaction with { Scopes = scopes ?? Transaction.Scopes });
            Assert.True(prep.StatusCode == HttpStatusCode.OK, await prep.Content.ReadAsStringAsync());
            var data = await prep.Content.ReadFromJsonAsync<JsonElement>(); var url = data.GetProperty("authorizeUrl").GetString(); Assert.NotNull(url);
            var response = await Client.GetAsync(url); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
            Assert.True(query.ContainsKey("code"), response.Headers.Location.ToString()); return query["code"].ToString();
        }
        public Task<HttpResponseMessage> Exchange(string code, string? verifier = null, string? secret = null, string? redirect = null) => Client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = "fixture-client", ["client_secret"] = secret ?? Secret, ["code"] = code, ["redirect_uri"] = redirect ?? Transaction.RedirectUri, ["code_verifier"] = verifier ?? Verifier }));
        public async Task<(HttpStatusCode Status, string AccessToken, string IdToken)> Login(string[]? scopes = null)
        {
            var response = await Exchange(await Code(scopes)); var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(response.IsSuccessStatusCode, body.GetRawText());
            return (response.StatusCode, body.GetProperty("access_token").GetString()!, body.GetProperty("id_token").GetString()!);
        }
        public Task<HttpResponseMessage> Get(string path, string? token, bool internalHost = false, string method = "GET")
        {
            var request = new HttpRequestMessage(new HttpMethod(method), (internalHost ? "https://internal.example" : "https://openapi.example") + path);
            if (token is not null) request.Headers.Authorization = new("Bearer", token); return Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); await RegistryConnection.DisposeAsync(); await ProtocolConnection.DisposeAsync(); }
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
}
