using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using viora_BE.Developer;
using viora_BE.Sso;

internal static class SandboxIdentityBrowserFixture
{
    public static async Task Run()
    {
        const string issuer = "https://identity.ankt.example.com:5490/", portal = "https://portal.ankt.localhost:5494", partner = "https://partner.ankt.example.com";
        var directory = Path.Combine(Path.GetTempPath(), "ankt-sandbox-identity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        await using var database = await IsolatedPostgres.Start();
        using var certificates = new FixtureCertificates(directory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        var bffKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sso:Enabled"] = "true", ["Sso:Issuer"] = issuer, ["Sso:FirstPartyOrigin"] = portal,
            ["Sso:SigningCertificatePath"] = certificates.SigningPath, ["Sso:EncryptionCertificatePath"] = certificates.EncryptionPath,
            ["DeveloperAuth:PortalUrl"] = portal + "/developers", ["DeveloperAuth:BffSharedSecret"] = bffKey,
            ["SandboxTest:PortalOrigin"] = portal, ["SandboxTest:Enabled"] = "true"
        });
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 5490, l => l.UseHttps(certificates.Tls)));
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.ConnectionString));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(); builder.Services.AddAuthorization();
        builder.Services.AddDeveloperAccounts(builder.Configuration); builder.Services.AddDeveloperMiniApps(builder.Configuration); builder.Services.AddAnktSso(builder.Configuration);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(Options.Create(new MiniAppOptions { ActiveKeyVersion = "fixture", EncryptionKeys = new() { ["fixture"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } }));
        builder.Services.AddControllers().AddApplicationPart(typeof(SsoAuthorizationController).Assembly);
        await using var app = builder.Build();
        var accounts = new List<object>();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.MigrateAsync();
            foreach (var key in new[] { 'a', 'b' })
            {
                var password = "Fixture-" + key + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
                var owner = new DeveloperAccount { FullName = "Developer " + key, EmailNormalized = ("developer-" + key + "@example.test").ToUpperInvariant(),
                    Status = DeveloperStatus.Active, EmailVerifiedAt = DateTime.UtcNow, TermsAcceptedAt = DateTime.UtcNow };
                owner.PasswordHash = new PasswordHasher<DeveloperAccount>(Options.Create(new PasswordHasherOptions { IterationCount = 210000 })).HashPassword(owner, password);
                var mini = new MiniApp { Developer = owner, AppId = "app_" + new string(key, 43), Name = "Sandbox app " + key, Category = "utilities",
                    SsoEnabled = true, WebsiteUrl = partner + "/" + key, CreateRequestId = Guid.NewGuid() };
                var client = new MiniAppOAuthClient { MiniApp = mini, ClientKey = "sandbox-identity-" + key, ClientType = "Confidential", SandboxEnabled = true };
                var secret = "cs_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var encrypted = scope.ServiceProvider.GetRequiredService<MiniAppSecretProtector>().Encrypt(secret, client.Id);
                db.MiniAppOAuthClients.Add(client);
                db.MiniAppClientSecrets.Add(new() { OAuthClient = client, SecretEncrypted = encrypted.Ciphertext, SecretKeyVersion = encrypted.Version });
                db.MiniAppRedirectUris.Add(new() { MiniApp = mini, RedirectUri = partner + "/" + key + "/callback" });
                db.MiniAppDomainVerifications.Add(new() { MiniApp = mini, Hostname = "partner.ankt.example.com", VerificationFileName = "fixture-" + key + ".txt",
                    Status = MiniAppDomainStatus.Verified, ExpiresAt = DateTime.UtcNow.AddHours(1) });
                await db.SaveChangesAsync();
                await using var transaction = await db.Database.BeginTransactionAsync();
                await scope.ServiceProvider.GetRequiredService<MiniAppOidcRegistry>().SynchronizeAsync(mini.Id, default); await transaction.CommitAsync();
                accounts.Add(new { key = key.ToString(), appId = mini.AppId, email = owner.EmailNormalized.ToLowerInvariant(), password, clientId = client.ClientKey, clientSecret = secret });
            }
        }
        app.UseDeveloperAccountBoundary(); app.UseRouting(); app.UseCors(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.UseAntiforgery();
        app.MapControllers(); app.MapDeveloperEndpoints(); app.MapDeveloperAccountEndpoints(); app.MapDeveloperMiniApps();
        app.MapGet("/fixture/health", () => Results.Ok());
        await File.WriteAllTextAsync(Path.Combine(directory, "identity-fixture.json"), JsonSerializer.Serialize(new { issuer, portal, partner, bffKey, accounts }));
        _ = Task.Run(async () => { if (await Console.In.ReadLineAsync() == "stop") app.Lifetime.StopApplication(); });
        try { await app.StartAsync(); Console.WriteLine("Sandbox identity fixture: " + directory); await app.WaitForShutdownAsync(); }
        finally
        {
            await using var evidenceScope = app.Services.CreateAsyncScope();
            var evidenceDb = evidenceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = await evidenceDb.SandboxIdentityTemplates.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.Account).ThenInclude(x => x.User).SingleOrDefaultAsync();
            var evidence = new
            {
                environment = "Development; isolated disposable local PostgreSQL",
                capturedAt = DateTime.UtcNow,
                accountId = template?.AccountId, userId = template?.Account.User?.Id,
                template?.FullName, username = template?.Nickname,
                email = template?.Account.Email, displayName = template?.Account.User?.DisplayName,
                avatarUrl = template?.Account.User?.AvatarUrl, coverUrl = template?.Account.User?.CoverUrl,
                isSandbox = template?.Account.IsSandbox, isVerified = template?.Account.User?.IsVerified,
                sandboxAccountCount = await evidenceDb.Accounts.IgnoreQueryFilters().CountAsync(x => x.IsSandbox),
                mappings = await evidenceDb.SandboxTestIdentities.AsNoTracking()
                    .Select(x => new { x.DeveloperAccountId, x.MiniAppId, x.AccountId }).ToArrayAsync(),
                sessions = await evidenceDb.SandboxIdentitySessions.AsNoTracking()
                    .Select(x => new { x.Id, x.IdentityId, x.ExpiresAt, x.RevokedAt, x.AuthorizedAt, x.TokenIssuedAt, x.UserInfoAt }).ToArrayAsync()
            };
            await File.WriteAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), ".codex-tmp", "sandbox-auto-database.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
