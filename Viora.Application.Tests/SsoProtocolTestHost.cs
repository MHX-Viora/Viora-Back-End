using System.Security.Cryptography;
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
    public Guid AccountId => accountId;
    public async Task<T> WithServices<T>(Func<IServiceProvider,Task<T>> action)
    {
        await using var scope = app.Services.CreateAsyncScope(); return await action(scope.ServiceProvider);
    }

    public static async Task<SsoProtocolTestHost> Create(IsolatedPostgres? postgres = null, Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        var host = new SsoProtocolTestHost(); await host.connection.OpenAsync();
        host.connection.CreateFunction<string?,int>("char_length", x => x?.Length ?? 0);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(host.certificates.Config());
        builder.Services.AddDbContext<AppDbContext>(x =>
        {
            if (postgres == null) x.UseSqlite(host.connection); else x.UseNpgsql(postgres.ConnectionString);
            configureDatabase?.Invoke(x);
        });
        builder.Services.AddSingleton(Options.Create(new MiniAppOptions { ActiveKeyVersion = "test", EncryptionKeys = new() { ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } }));
        builder.Services.AddSingleton<MiniAppSecretProtector>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(); builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(SsoAuthorizationController).Assembly);
        builder.Services.AddAnktSso(builder.Configuration);
        builder.Services.AddCors(options => options.AddPolicy("Web", policy => policy.WithOrigins("https://firstparty.example")
            .AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
        host.app = builder.Build(); host.app.UseRouting(); host.app.UseCors("Web"); host.app.UseAuthentication(); host.app.UseRateLimiter(); host.app.UseAuthorization(); host.app.MapControllers();
        await host.app.StartAsync(); host.Client = host.app.GetTestClient(); host.Client.BaseAddress = new("https://issuer.example");
        await using var scope = host.app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.EnsureCreatedAsync();
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
