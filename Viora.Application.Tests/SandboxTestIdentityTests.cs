using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Security;
using Viora.Infrastructure.Sso;
using viora_BE.Developer;
using Xunit;

public sealed class SandboxTestIdentityTests
{
    [Fact]
    public async Task OwnerLaunchAuthorizesWithoutConsentAndReauthorizesAfterAccessExpiry()
    {
        var clock = new ProtocolClock();
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true, timeProvider: clock,
            configureSso: values => values["Sso:AccessTokenSeconds"] = "30");
        var owner = await Developer(host);
        var cookie = await TestCookie(host, await Launch(host, owner));
        host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        var response = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "none" }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.True(query.ContainsKey("code"), response.Headers.Location.ToString());
        var exchange = await SsoTokenEndpointTests.Exchange(host, query["code"]!);
        var tokens = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(tokens.TryGetProperty("refresh_token", out _));
        clock.Now = clock.Now.AddSeconds(31);
        var next = await host.Client.GetAsync(host.AuthorizationUrl(new() { ["prompt"] = "none" }));
        Assert.True(QueryHelpers.ParseQuery(next.Headers.Location!.Query).ContainsKey("code"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task ProductionOrDisabledSandboxDoesNotSeedOrLaunch(string environment)
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true, environment: environment,
            configureSso: values => values["SandboxTest:Enabled"] = environment == "Production" ? "true" : "false");
        var owner = await Developer(host);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => s.GetRequiredService<SandboxTestService>()
            .OverviewAsync(owner.Owner, owner.Source, "app_protocol", default)));
        await host.WithServices(async s => {
            Assert.Equal(0, await s.GetRequiredService<AppDbContext>().Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
            return true;
        });
    }

    private sealed class ProtocolClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal static async Task<(Guid Owner, Guid Source)> Developer(SsoProtocolTestHost host) => await host.WithServices(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>(); var owner = await db.DeveloperAccounts.SingleAsync();
        var source = new DeveloperSession { DeveloperAccountId = owner.Id, TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddHours(1), AbsoluteExpiresAt = DateTime.UtcNow.AddHours(1), LastUsedAt = DateTime.UtcNow };
        db.DeveloperSessions.Add(source); await db.SaveChangesAsync(); return (owner.Id, source.Id);
    });
    internal static Task<SandboxTestLaunch> Launch(SsoProtocolTestHost host, (Guid Owner, Guid Source) developer) =>
        host.WithServices(s => s.GetRequiredService<SandboxTestService>().CreateAsync(developer.Owner, developer.Source, "app_protocol", default));

    [Fact]
    public async Task AccountsAndUsersPersistTemplateAndStableScopedIdentityWithoutProductionVisibility()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var owner = await Developer(host);
        var first = await Launch(host, owner); var second = await Launch(host, owner);
        Assert.NotEqual(first.LaunchTicket, second.LaunchTicket); Assert.NotEqual(first.SessionId, second.SessionId);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
            Assert.Equal(1, await db.SandboxTestIdentities.CountAsync());
            Assert.Equal(1, await db.Accounts.CountAsync()); Assert.Equal(0, await db.Users.CountAsync());
            var profile = (await services.GetRequiredService<SandboxTestService>().OverviewAsync(owner.Owner, owner.Source, "app_protocol", default)).Profile;
            Assert.Equal("ANKT Developer Test", profile.FullName); Assert.Equal("ANKT Sandbox User", profile.DisplayName);
            Assert.Equal("ankt_sandbox_test", profile.Nickname); Assert.Equal("2000-01-01", profile.Birthdate);
            Assert.Equal("sandbox-test@domain.test", profile.Email); Assert.False(profile.EmailVerified);
            Assert.True(SandboxTestOptions.CloudinaryUrl(profile.AvatarUrl)); Assert.True(SandboxTestOptions.CloudinaryUrl(profile.CoverUrl));
            var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(x => x.Id == db.SandboxTestIdentities.Select(i => i.AccountId).First());
            Assert.Null(account.PasswordHash); Assert.Null(account.EmailVerifiedAt);
            var jwt = new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 64) }));
            Assert.Throws<InvalidOperationException>(() => jwt.CreateTokens(account));
            await Assert.ThrowsAsync<DeveloperAuthException>(() => services.GetRequiredService<SsoSessionService>().CreateAsync(account.Id, Guid.NewGuid(), default));
            return true;
        });
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => s.GetRequiredService<SandboxTestService>()
            .CreateAsync(Guid.NewGuid(), owner.Source, "app_protocol", default)));
    }

    [Fact]
    public async Task LaunchIsSingleUseAndExpiredOrRevokedDeveloperSessionsInvalidateRuntime()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var owner = await Developer(host); var launch = await Launch(host, owner);
        var started = await host.WithServices(s => s.GetRequiredService<SandboxTestService>().ConsumeAsync(launch.LaunchTicket, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => s.GetRequiredService<SandboxTestService>().ConsumeAsync(launch.LaunchTicket, default)));
        Assert.NotNull(await host.WithServices(s => s.GetRequiredService<SandboxTestService>().AuthenticateAsync(started.Credential, SsoProtocolTestHost.ClientId, default)));
        Assert.Null(await host.WithServices(s => s.GetRequiredService<SandboxTestService>().AuthenticateAsync(started.Credential, "another-client", default)));
        await host.WithServices(async s => { await s.GetRequiredService<AppDbContext>().DeveloperSessions.Where(x => x.Id == owner.Source)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.RevokedAt, DateTime.UtcNow)); return true; });
        Assert.Null(await host.WithServices(s => s.GetRequiredService<SandboxTestService>().AuthenticateAsync(started.Credential, SsoProtocolTestHost.ClientId, default)));
    }

    internal static async Task<string> TestCookie(SsoProtocolTestHost host, SandboxTestLaunch launch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sso/sandbox-test/start")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["launch_ticket"] = launch.LaunchTicket }) };
        request.Headers.Add("Origin", "https://mxh.ankt.vn");
        var response = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("https://partner.example.com", await response.Content.ReadAsStringAsync());
        var header = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(SandboxTestApi.ClientCookie(SsoProtocolTestHost.ClientId) + "="));
        Assert.Contains("secure", header); Assert.Contains("httponly", header); Assert.Contains("samesite=lax", header);
        return header.Split(';')[0];
    }

    internal static async Task<string> TestCode(SsoProtocolTestHost host, string cookie, string scopes = "openid profile email")
    {
        host.Client.DefaultRequestHeaders.Remove("Cookie"); host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var request = new HttpRequestMessage(HttpMethod.Get, host.AuthorizationUrl(new() { ["scope"] = scopes }));
        request.Headers.Accept.ParseAdd("application/json");
        var response = await host.Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Redirect)
            return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var values = prompt.GetProperty("parameters").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!);
        values["__RequestVerificationToken"] = prompt.GetProperty("csrfToken").GetString()!;
        values["consent_ticket"] = prompt.GetProperty("consentTicket").GetString()!; values["decision"] = "allow";
        var csrf = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-AnktSsoCsrf="));
        host.Client.DefaultRequestHeaders.Remove("Cookie"); host.Client.DefaultRequestHeaders.Add("Cookie", cookie + "; " + csrf.Split(';')[0]);
        var accepted = await host.Client.PostAsync("/connect/authorize", new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        return QueryHelpers.ParseQuery(accepted.Headers.Location!.Query)["code"].ToString();
    }

    [Fact]
    public async Task RealCodePkceAndUserInfoReturnScopedSandboxProfileAndReopenRetainsSubject()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var owner = await Developer(host);
        async Task<(string Subject, string Access)> Login()
        {
            var cookie = await TestCookie(host, await Launch(host, owner)); var code = await TestCode(host, cookie);
            var response = await SsoTokenEndpointTests.Exchange(host, code); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var token = await response.Content.ReadFromJsonAsync<JsonElement>(); var access = token.GetProperty("access_token").GetString()!;
            using var info = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); info.Headers.Authorization = new("Bearer", access);
            var result = await host.Client.SendAsync(info); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var profile = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ANKT Developer Test", profile.GetProperty("name").GetString());
            Assert.Equal("ankt_sandbox_test", profile.GetProperty("preferred_username").GetString());
            Assert.Equal("ankt_sandbox_test", profile.GetProperty("nickname").GetString());
            Assert.True(profile.GetProperty("isSandbox").GetBoolean());
            await host.WithServices(async s => {
                var db = s.GetRequiredService<AppDbContext>();
                var id = await db.SandboxIdentityTemplates.Select(x => x.AccountId).SingleAsync();
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.AccountId == id);
                Assert.Equal(user.Id.ToString(), profile.GetProperty("id").GetString());
                Assert.Equal(user.DisplayName, profile.GetProperty("displayName").GetString());
                Assert.True(user.IsVerified); return true;
            });
            Assert.Equal("male", profile.GetProperty("gender").GetString()); Assert.Equal("2000-01-01", profile.GetProperty("birthdate").GetString());
            Assert.False(profile.GetProperty("email_verified").GetBoolean());
            Assert.True(SandboxTestOptions.CloudinaryUrl(profile.GetProperty("picture").GetString()));
            Assert.True(SandboxTestOptions.CloudinaryUrl(profile.GetProperty("ankt_cover_picture").GetString()));
            Assert.NotEqual(host.AccountId.ToString(), profile.GetProperty("sub").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await SsoTokenEndpointTests.Exchange(host, code)).StatusCode);
            return (profile.GetProperty("sub").GetString()!, access);
        }
        var first = await Login(); var second = await Login(); Assert.Equal(first.Subject, second.Subject); Assert.NotEqual(first.Access, second.Access);
        var observed = await host.WithServices(s => s.GetRequiredService<SandboxTestService>().OverviewAsync(owner.Owner, owner.Source, "app_protocol", default));
        Assert.Equal(2, observed.Sessions.Length);
        Assert.All(observed.Sessions, session => { Assert.NotNull(session.AuthorizedAt); Assert.NotNull(session.TokenIssuedAt); Assert.NotNull(session.UserInfoAt); });
        await host.WithServices(async s => { await s.GetRequiredService<AppDbContext>().DeveloperSessions.Where(x => x.Id == owner.Source)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.RevokedAt, DateTime.UtcNow)); return true; });
        using var revoked = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); revoked.Headers.Authorization = new("Bearer", second.Access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(revoked)).StatusCode);
    }

    [Fact]
    public async Task OpenidOnlyDoesNotReleaseProfileAndWrongPkceNeverIssuesTokens()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var owner = await Developer(host);
        var cookie = await TestCookie(host, await Launch(host, owner)); var code = await TestCode(host, cookie, "openid");
        Assert.Equal(HttpStatusCode.BadRequest, (await SsoTokenEndpointTests.Exchange(host, code, new() { ["code_verifier"] = new string('b',64) })).StatusCode);
        var valid = await TestCode(host, cookie, "openid"); var response = await SsoTokenEndpointTests.Exchange(host, valid);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var info = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); info.Headers.Authorization = new("Bearer", tokens.GetProperty("access_token").GetString());
        var result = await host.Client.SendAsync(info); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Single((await result.Content.ReadFromJsonAsync<JsonElement>()).EnumerateObject());
    }

    [Fact]
    public async Task DifferentDevelopersAndAppsShareAccountWithIsolatedSessionsAndCannotLaunchEachOthersIdentity()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var firstOwner = await Developer(host);
        var launch = await Launch(host, firstOwner);
        var second = await host.WithServices(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var developer = new DeveloperAccount { FullName = "Developer B", EmailNormalized = "b@example.test", Status = DeveloperStatus.Active };
            var app = new MiniApp { Developer = developer, AppId = "app_b", Name = "B", WebsiteUrl = "https://b.partner.example.com", Category = "utilities", SsoEnabled = true };
            var client = new MiniAppOAuthClient { MiniApp = app, ClientKey = "client-b", ClientType = "Public", SandboxEnabled = true };
            var source = new DeveloperSession { Account = developer, TokenHash = "b-session", ExpiresAt = DateTime.UtcNow.AddHours(1), AbsoluteExpiresAt = DateTime.UtcNow.AddHours(1) };
            db.MiniAppOAuthClients.Add(client); db.DeveloperSessions.Add(source);
            db.MiniAppDomainVerifications.Add(new() { MiniApp = app, Hostname = "b.partner.example.com", VerificationFileName = "b-fixture.txt", Status = MiniAppDomainStatus.Verified, ExpiresAt = DateTime.UtcNow.AddDays(1) });
            db.MiniAppRedirectUris.Add(new() { MiniApp = app, RedirectUri = "https://b.partner.example.com/callback" });
            await db.SaveChangesAsync();
            var readiness = await s.GetRequiredService<SandboxTestService>().OverviewAsync(developer.Id, source.Id, "app_b", default);
            Assert.True(readiness.Ready, readiness.ErrorCode);
            var created = await s.GetRequiredService<SandboxTestService>().CreateAsync(developer.Id, source.Id, "app_b", default);
            Assert.Equal(2, await db.SandboxTestIdentities.CountAsync());
            Assert.Equal(1, await db.SandboxTestIdentities.Select(x => x.AccountId).Distinct().CountAsync());
            Assert.Equal(1, await db.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
            return (developer.Id, source.Id, created);
        });
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => s.GetRequiredService<SandboxTestService>()
            .CreateAsync(firstOwner.Owner, firstOwner.Source, "app_b", default)));
        var firstBrowser = await host.WithServices(s => s.GetRequiredService<SandboxTestService>().ConsumeAsync(launch.LaunchTicket, default));
        Assert.Null(await host.WithServices(s => s.GetRequiredService<SandboxTestService>().AuthenticateAsync(firstBrowser.Credential, "client-b", default)));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(async s => {
            await s.GetRequiredService<SandboxTestService>().RevokeAsync(second.Item1, second.Item2, "app_b", launch.SessionId, default); return true;
        }));
    }

    [Fact]
    public async Task ExpiredLaunchAndRuntimeFailClosedAndMissingImagesAreReported()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var owner = await Developer(host); var launch = await Launch(host, owner);
        await host.WithServices(async s => { await s.GetRequiredService<AppDbContext>().SandboxIdentitySessions.Where(x => x.Id == launch.SessionId)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.LaunchExpiresAt, DateTime.UtcNow.AddMinutes(-1))); return true; });
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => s.GetRequiredService<SandboxTestService>().ConsumeAsync(launch.LaunchTicket, default)));
        var nextLaunch = await Launch(host, owner);
        var started = await host.WithServices(s => s.GetRequiredService<SandboxTestService>().ConsumeAsync(nextLaunch.LaunchTicket, default));
        await host.WithServices(async s => { await s.GetRequiredService<AppDbContext>().SandboxIdentitySessions.ExecuteUpdateAsync(x => x.SetProperty(y => y.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))); return true; });
        Assert.Null(await host.WithServices(s => s.GetRequiredService<SandboxTestService>().AuthenticateAsync(started.Credential, SsoProtocolTestHost.ClientId, default)));
        await using var missing = await SsoProtocolTestHost.Create(testIdentity: true, configureSso: values => { values["SandboxTest:AvatarUrl"] = ""; values["SandboxTest:CoverUrl"] = ""; });
        var missingOwner = await Developer(missing);
        var overview = await missing.WithServices(s => s.GetRequiredService<SandboxTestService>().OverviewAsync(missingOwner.Owner, missingOwner.Source, "app_protocol", default));
        Assert.False(overview.Ready); Assert.Equal("SANDBOX_ASSETS_MISSING", overview.ErrorCode);
    }

    [Fact]
    public async Task PostgreSqlMigrationAndRealProtocolWorkWithPersistedSandboxAccounts()
    {
        await using var postgres = await IsolatedPostgres.Start();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options))
            await db.Database.MigrateAsync();
        await using var host = await SsoProtocolTestHost.Create(postgres, testIdentity: true); var owner = await Developer(host);
        var cookie = await TestCookie(host, await Launch(host, owner)); var code = await TestCode(host, cookie);
        Assert.Equal(HttpStatusCode.OK, (await SsoTokenEndpointTests.Exchange(host, code)).StatusCode);
        await using var check = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var current = check.Database.GetMigrations().Single(x => x.EndsWith("_AddSandboxTestIdentities"));
        var previous = check.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => check.GetService<IMigrator>().MigrateAsync(previous));
        Assert.Equal(1, await check.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
    }

    [Fact]
    public async Task TestModeNeverUsesAProductionSocialIdentityAndLaunchRequiresExactPortalOrigin()
    {
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true); var owner = await Developer(host);
        host.Client.DefaultRequestHeaders.Add("Cookie", await host.UserCookie());
        var response = await host.Client.GetAsync(host.AuthorizationUrl());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("login_required", QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString());
        Assert.DoesNotContain("/sso/start", response.Headers.Location.AbsoluteUri);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => s.GetRequiredService<SsoConsentService>()
            .GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "profile"], default)));
        var launch = await Launch(host, owner);
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/sso/sandbox-test/start")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["launch_ticket"] = launch.LaunchTicket }) };
        forged.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(forged)).StatusCode);
        Assert.False(string.IsNullOrEmpty(await TestCookie(host, launch)));
        await using var disabled = await SsoProtocolTestHost.Create(testIdentity: true,
            configureSso: values => values["SandboxTest:Enabled"] = "false");
        disabled.Client.DefaultRequestHeaders.Add("Cookie", await disabled.UserCookie());
        var closed = await disabled.Client.GetAsync(disabled.AuthorizationUrl());
        Assert.Equal("login_required", QueryHelpers.ParseQuery(closed.Headers.Location!.Query)["error"].ToString());
        await Assert.ThrowsAsync<DeveloperAuthException>(() => disabled.WithServices(s => s.GetRequiredService<SsoConsentService>()
            .GrantAsync(disabled.AccountId, SsoProtocolTestHost.ClientId, ["openid", "profile"], default)));
    }

    [Fact]
    public async Task WrongIssuerAudienceExpiredTokensInvalidRedirectAndProductionApiAreRejected()
    {
        var clock = new ProtocolClock();
        await using var host = await SsoProtocolTestHost.Create(testIdentity: true, timeProvider: clock,
            configureSso: values => values["Sso:AccessTokenSeconds"] = "30"); var owner = await Developer(host);
        var cookie = await TestCookie(host, await Launch(host, owner)); var code = await TestCode(host, cookie);
        var response = await SsoTokenEndpointTests.Exchange(host, code); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>(); var access = tokens.GetProperty("access_token").GetString()!;
        Assert.InRange(tokens.GetProperty("expires_in").GetInt32(), 1, 30);
        var keys = new JsonWebKeySet(await host.Client.GetStringAsync("/.well-known/jwks.json"));
        foreach (var (issuer, audience) in new[] { ("https://wrong.example/", SsoProtocolTestHost.ClientId), ("https://issuer.example/", "another-client") })
        {
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.GetProperty("id_token").GetString(), new TokenValidationParameters
            { ValidIssuer = issuer, ValidAudience = audience, IssuerSigningKeys = keys.GetSigningKeys(), ValidateIssuerSigningKey = true,
                ValidateLifetime = true, ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero });
            Assert.False(result.IsValid);
        }
        using var production = new HttpRequestMessage(HttpMethod.Get, "/fixture/production-account"); production.Headers.Authorization = new("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(production)).StatusCode);
        using var validProduction = new HttpRequestMessage(HttpMethod.Get, "/fixture/production-account"); validProduction.Headers.Authorization = new("Bearer", host.SocialAccessToken());
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(validProduction)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(host.AuthorizationUrl(new() { ["redirect_uri"] = "https://evil.example/callback" }))).StatusCode);
        // Expiry is authenticated in the protected token payload, not simulated
        // by editing its database metadata. Advance the actual protocol clock.
        clock.Now += TimeSpan.FromMinutes(10);
        using var expired = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); expired.Headers.Authorization = new("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(expired)).StatusCode);
    }

    [Fact]
    public async Task PostgreSqlHostRestartPreservesAccountSubjectAndCloudinaryUrls()
    {
        await using var postgres = await IsolatedPostgres.Start();
        async Task<(Guid Account, string Subject, string? Avatar, string? Cover)> Run(SsoProtocolTestHost host)
        {
            var owner = await Developer(host); var cookie = await TestCookie(host, await Launch(host, owner));
            var response = await SsoTokenEndpointTests.Exchange(host, await TestCode(host, cookie)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"); request.Headers.Authorization = new("Bearer", tokens.GetProperty("access_token").GetString());
            var info = await host.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, info.StatusCode);
            var profile = await info.Content.ReadFromJsonAsync<JsonElement>();
            var account = await host.WithServices(async s =>
            {
                var db = s.GetRequiredService<AppDbContext>(); Assert.Equal(1, await db.SandboxIdentityTemplates.CountAsync());
                Assert.Equal(1, await db.SandboxTestIdentities.CountAsync()); Assert.Equal(1, await db.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox));
                return await db.SandboxTestIdentities.Select(x => x.AccountId).SingleAsync();
            });
            return (account, profile.GetProperty("sub").GetString()!, profile.GetProperty("picture").GetString(), profile.GetProperty("ankt_cover_picture").GetString());
        }
        (Guid Account, string Subject, string? Avatar, string? Cover) first;
        await using (var before = await SsoProtocolTestHost.Create(postgres, testIdentity: true)) first = await Run(before);
        // Disposed/recreated WebApplication, DI container, DbContext and crypto
        // credentials; only the isolated PostgreSQL data survives the restart.
        await using var after = await SsoProtocolTestHost.Create(postgres, testIdentity: true, reuseSeed: true);
        var reopened = await Run(after); Assert.Equal(first, reopened);
    }
}
