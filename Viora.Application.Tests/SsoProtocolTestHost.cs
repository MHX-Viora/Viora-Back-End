using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using viora_BE.Sso;
using viora_BE.Hosting;

internal sealed class SsoProtocolTestHost : IAsyncDisposable
{
    private readonly SsoRegistrationTests.Certificates certificates = new();
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private WebApplication app = null!;
    public HttpClient Client = null!;
    public const string ClientId = "public-protocol-test";
    public const string RedirectUri = "https://partner.example.com/callback";
    public const string Verifier = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private Guid accountId, socialId, miniAppId;
    private readonly SymmetricSecurityKey socialKey = new(RandomNumberGenerator.GetBytes(32));
    public string SocialAccessToken(Guid? owner = null, string tokenType = "access") => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://social.example", Audience = "ankt-social", Expires = DateTime.UtcNow.AddMinutes(5),
        Subject = new ClaimsIdentity([new Claim("sub", (owner ?? accountId).ToString()), new Claim("sid", socialId.ToString()), new Claim("token_type", tokenType)]),
        SigningCredentials = new(socialKey, SecurityAlgorithms.HmacSha256)
    });
    public Guid AccountId => accountId;
    public async Task<T> WithServices<T>(Func<IServiceProvider,Task<T>> action)
    {
        await using var scope = app.Services.CreateAsyncScope(); return await action(scope.ServiceProvider);
    }

    public static async Task<SsoProtocolTestHost> Create(IsolatedPostgres? postgres = null, Action<DbContextOptionsBuilder>? configureDatabase = null,
        bool allowIntrospection = false, Action<Dictionary<string, string?>>? configureSso = null, TimeProvider? timeProvider = null, bool sandbox=false,
        System.Net.IPAddress? proxyPeer = null, bool testIdentity = false, bool reuseSeed = false, string environment = "Testing")
    {
        var host = new SsoProtocolTestHost(); await host.connection.OpenAsync();
        host.connection.CreateFunction<string?,int>("char_length", x => x?.Length ?? 0);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        var ssoConfiguration = host.certificates.Config();
        ssoConfiguration["SandboxTest:Enabled"] = testIdentity ? "true" : "false";
        configureSso?.Invoke(ssoConfiguration);
        builder.Configuration.AddInMemoryCollection(ssoConfiguration);
        if (allowIntrospection) builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Sso:IntrospectionClientIds:0"] = ClientId });
        builder.Services.AddDbContext<AppDbContext>(x =>
        {
            if (postgres == null) x.UseSqlite(host.connection); else x.UseNpgsql(postgres.ConnectionString);
            configureDatabase?.Invoke(x);
        });
        builder.Services.AddSingleton(Options.Create(new MiniAppOptions { ActiveKeyVersion = "test", EncryptionKeys = new() { ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } }));
        builder.Services.AddSingleton<MiniAppSecretProtector>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new() { ValidIssuer = "https://social.example", ValidAudience = "ankt-social",
                IssuerSigningKey = host.socialKey, ValidateLifetime = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256] };
            options.Events = new() { OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("token_type")?.Value != "access") context.Fail("Invalid token type.");
                return Task.CompletedTask;
            } };
        }); builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(SsoAuthorizationController).Assembly);
        if (timeProvider != null) builder.Services.AddSingleton(timeProvider);
        builder.Services.AddAnktSso(builder.Configuration);
        if (!testIdentity)
        {
            // These pre-existing tests exercise generic real-user OIDC consent/code
            // behavior in isolation. The new Sandbox identity boundary is exercised
            // with the full application registrations when testIdentity is true.
            // No deployment flag permits real-user fallback in production.
            builder.Services.AddScoped<SsoConsentService>(s => new(s.GetRequiredService<AppDbContext>(),
                s.GetRequiredService<SsoClientReader>(), s.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>(),
                s.GetRequiredService<OpenIddict.Abstractions.IOpenIddictAuthorizationManager>(), s.GetRequiredService<OpenIddict.Abstractions.IOpenIddictTokenManager>(),
                s.GetRequiredService<TimeProvider>()));
            builder.Services.AddControllers().AddControllersAsServices();
            builder.Services.AddScoped<SsoAuthorizationController>(s => new(s.GetRequiredService<SsoClientReader>(),
                s.GetRequiredService<SsoConsentService>(), s.GetRequiredService<SsoConsentInteraction>(),
                s.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>(), s.GetRequiredService<SandboxProtocolObserver>()));
        }
        builder.Services.AddTrustedProxyHeaders(builder.Configuration);
        if(sandbox){
            builder.Services.AddSingleton(new MiniAppSandboxOptions{Enabled=true,HostOrigin="https://sandbox.ankt.example",PortalOrigin="https://portal.ankt.example",Issuer="https://issuer.example/"});
            builder.Services.AddScoped<MiniAppSandboxAccess>();builder.Services.AddScoped<MiniAppSandboxSessionService>();
            builder.Services.AddScoped<MiniAppSandboxProtocolService>();
        }
        builder.Services.AddCors(options => options.AddPolicy("Web", policy => policy.WithOrigins("https://firstparty.example")
            .AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
        host.app = builder.Build();
        if (proxyPeer is not null) host.app.Use((context, next) => { context.Connection.RemoteIpAddress = proxyPeer; return next(context); });
        host.app.UseTrustedProxyHeaders();
        host.app.UseRouting(); host.app.UseCors("Web"); host.app.UseAuthentication(); host.app.UseRateLimiter(); host.app.UseAuthorization(); host.app.MapControllers();
        host.app.MapGet("/fixture/production-account", () => Microsoft.AspNetCore.Http.Results.Ok())
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser());
        await host.app.StartAsync(); host.Client = host.app.GetTestClient(); host.Client.BaseAddress = new("https://issuer.example");
        await using var scope = host.app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
        if (reuseSeed)
        {
            host.accountId = await db.Accounts.Where(x => x.Email == "protocol-user@example.test").Select(x => x.Id).SingleAsync();
            host.socialId = (await db.RefreshTokens.Where(x => x.AccountId == host.accountId).Select(x => x.SessionId).FirstAsync())!.Value;
            host.miniAppId = await db.MiniApps.Where(x => x.AppId == "app_protocol").Select(x => x.Id).SingleAsync();
            return host;
        }
        var owner = new DeveloperAccount { FullName = "Protocol test", EmailNormalized = "protocol@example.test", Status = DeveloperStatus.Active, TermsAcceptedAt = DateTime.UtcNow };
        var account = new Account { Email = "protocol-user@example.test" }; host.accountId = account.Id; host.socialId = Guid.NewGuid();
        var mini = new MiniApp { Developer = owner, AppId = "app_protocol", Name = "Protocol app", Category = "utilities", SsoEnabled = true, WebsiteUrl = "https://partner.example.com", Status = MiniAppStatus.Draft };
        host.miniAppId = mini.Id;
        db.Accounts.Add(account); db.MiniApps.Add(mini);
        db.RefreshTokens.Add(new RefreshToken { AccountId = account.Id, SessionId = host.socialId, TokenHash = "protocol-test", ExpiresAt = DateTime.UtcNow.AddHours(2) });
        db.MiniAppOAuthClients.Add(new MiniAppOAuthClient { MiniAppId = mini.Id, ClientKey = ClientId, ClientType = "Public", SandboxEnabled = true });
        db.MiniAppRedirectUris.Add(new MiniAppRedirectUri { MiniAppId = mini.Id, RedirectUri = RedirectUri });
        db.MiniAppDomainVerifications.Add(new MiniAppDomainVerification { MiniAppId = mini.Id, Hostname = "partner.example.com", Status = MiniAppDomainStatus.Verified, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<MiniAppOidcRegistry>().SynchronizeAsync(mini.Id, default); await transaction.CommitAsync();
        return host;
    }

    public string AuthorizationUrl(Dictionary<string,string?>? changes = null)
    {
        var values = new Dictionary<string,string?> { ["client_id"] = ClientId, ["redirect_uri"] = RedirectUri, ["response_type"] = "code", ["scope"] = "openid profile", ["state"] = "protocol-test-state-unguessable", ["nonce"] = "protocol-test-nonce", ["code_challenge_method"] = "S256", ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(Verifier))) };
        if (changes != null) foreach (var pair in changes) values[pair.Key] = pair.Value;
        return QueryHelpers.AddQueryString("/connect/authorize", values);
    }
    public async Task MakeConfidential(string secret)
    {
        await WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>(); var client = await db.MiniAppOAuthClients.SingleAsync();
            client.ClientType = "Confidential";
            var encrypted = services.GetRequiredService<MiniAppSecretProtector>().Encrypt(secret, client.Id);
            db.MiniAppClientSecrets.Add(new() { OAuthClientId = client.Id, SecretEncrypted = encrypted.Ciphertext, SecretKeyVersion = encrypted.Version });
            await using var transaction = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync();
            await services.GetRequiredService<MiniAppOidcRegistry>().SynchronizeAsync(client.MiniAppId, default); await transaction.CommitAsync(); return true;
        });
    }
    public async Task<string> UserCookie()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<SsoSessionService>().CreateAsync(accountId, socialId, default);
        return SsoSessionHandler.CookieName + "=" + session.Credential;
    }
    public async Task ChangeAppStatus(MiniAppStatus status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().MiniApps.Where(x => x.Id == miniAppId).ExecuteUpdateAsync(x => x.SetProperty(m => m.Status, status));
    }
    public async ValueTask DisposeAsync() { Client?.Dispose(); if (app != null) await app.DisposeAsync(); await connection.DisposeAsync(); certificates.Dispose(); }
}
