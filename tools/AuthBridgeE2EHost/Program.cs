using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using viora_BE.Developer;

// No appsettings, .env, external services or live database are loaded.
const string api = "https://identity.ankt.example.com:5490", portal = "https://portal.ankt.localhost:5494", partner = "https://partner.ankt.example.com";
var directory = Path.Combine(Path.GetTempPath(), "ankt-auth-bridge-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
await using var pg = await IsolatedPostgres.Start();
using var cert = new FixtureCertificates(directory);
var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
b.Configuration.Sources.Clear(); b.Logging.ClearProviders();
var bffKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
var partnerSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
    ["DeveloperAuth:PortalUrl"] = portal + "/developers", ["DeveloperAuth:BffSharedSecret"] = bffKey,
    ["SandboxTest:Enabled"] = "true", ["MiniAppAuth:Enabled"] = "true", ["MiniAppAuth:AllowedHostOrigin"] = portal
});
foreach (var letter in new[] { 'a', 'b' })
    foreach (var environment in new[] { "Sandbox", "Production" })
        b.Configuration[$"MiniAppAuth:Partners:app_{new string(letter, 43)}:{environment}SecretHash"] = MiniAppBridgeService.Hash(partnerSecret);
b.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 5490, l => l.UseHttps(cert.Tls)));
b.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(pg.ConnectionString));
b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new() { ValidIssuer = "https://social.fixture", ValidAudience = "ankt-fixture", IssuerSigningKey = key, ValidAlgorithms = [SecurityAlgorithms.HmacSha256] };
});
b.Services.AddAuthorization(); b.Services.AddDeveloperAccounts(b.Configuration); b.Services.AddDeveloperMiniApps(b.Configuration);
b.Services.AddSingleton(Options.Create(new MiniAppOptions { ActiveKeyVersion = "fixture", EncryptionKeys = new() { ["fixture"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } }));
await using var app = b.Build();
await using var nts = await NtsPartnerFixture.Create(pg.ConnectionString, api, partner, partnerSecret, cert.Tls);
var accounts = new List<object>(); string socialToken;
await using (var scope = app.Services.CreateAsyncScope()) {
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.MigrateAsync();
    var user = new Account { Email = "bridge-real-user@example.test", User = new User { DisplayName = "ANKT Real User" } };
    var admin = new Account { Email = "bridge-admin@example.test", Role = AccountRole.Admin }; var sid = Guid.NewGuid();
    db.AddRange(user, admin);
    db.RefreshTokens.Add(new() { Account = user, SessionId = sid, TokenHash = "fixture-social-source", ExpiresAt = DateTime.UtcNow.AddHours(1) });
    await db.SaveChangesAsync();
    socialToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = "https://social.fixture", Audience = "ankt-fixture", Expires = DateTime.UtcNow.AddMinutes(5),
        Subject = new ClaimsIdentity([new("sub", user.Id.ToString()), new("sid", sid.ToString()), new("token_type", "access")]), SigningCredentials = new(key, SecurityAlgorithms.HmacSha256) });
    foreach (var letter in new[] { 'a', 'b' }) {
        var password = "Fixture-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var owner = new DeveloperAccount { FullName = "Developer " + letter, EmailNormalized = ("bridge-" + letter + "@example.test").ToUpperInvariant(), Status = DeveloperStatus.Active, EmailVerifiedAt = DateTime.UtcNow, TermsAcceptedAt = DateTime.UtcNow };
        owner.PasswordHash = new PasswordHasher<DeveloperAccount>(Options.Create(new PasswordHasherOptions { IterationCount = 210000 })).HashPassword(owner, password);
        var mini = new MiniApp { Developer = owner, AppId = "app_" + new string(letter, 43), Name = "Bridge " + letter, Category = "utilities", WebsiteUrl = partner + "/" + letter };
        db.Add(mini); db.MiniAppDomainVerifications.Add(new() { MiniApp = mini, Hostname = "partner.ankt.example.com", VerificationFileName = "fixture-" + letter + ".txt", Status = MiniAppDomainStatus.Verified, ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var snapshot = MiniAppVersionSnapshotPolicy.Create(new(1, mini.Name, mini.Category, null, "Fixture", null, mini.WebsiteUrl, "partner.ankt.example.com", partner + "/privacy", partner + "/terms", null, false, null, null));
        var version = new MiniAppVersion { MiniAppId = mini.Id, CreatedByDeveloperId = owner.Id, VersionCode = 1, VersionName = "1.0", SnapshotJson = snapshot.Json, SnapshotHash = snapshot.Hash, State = MiniAppVersionState.Submitted, SubmittedAt = DateTime.UtcNow };
        db.Add(version); await db.SaveChangesAsync();
        var review = new MiniAppReviewRequest { MiniAppId = mini.Id, MiniAppVersionId = version.Id, DeveloperAccountId = owner.Id, Status = MiniAppReviewStatus.PendingReview, SubmittedAt = DateTime.UtcNow, EvidenceAttestationJson = "{}", CorrelationId = Guid.NewGuid() };
        db.Add(review); await db.SaveChangesAsync();
        review.Status = MiniAppReviewStatus.InReview; review.AssignedAdminAccountId = admin.Id; review.ReviewStartedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        review.Status = MiniAppReviewStatus.Approved; review.DecidedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        db.AddRange(new MiniAppPublication { MiniAppId = mini.Id, MiniAppVersionId = version.Id, ApprovedReviewRequestId = review.Id, PublishedByDeveloperId = owner.Id, PublishedAt = DateTime.UtcNow }, new MiniAppOperationalState { MiniAppId = mini.Id });
        await db.SaveChangesAsync();
        accounts.Add(new { key = letter.ToString(), appId = mini.AppId, email = owner.EmailNormalized.ToLowerInvariant(), password });
    }
}
app.UseDeveloperAccountBoundary(); app.UseRouting(); app.UseCors(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
app.MapDeveloperEndpoints(); app.MapDeveloperAccountEndpoints(); app.MapDeveloperMiniApps(); app.MapMiniAppAuthBridge();
nts.Map(app);
await File.WriteAllTextAsync(Path.Combine(directory, "bridge-fixture.json"), JsonSerializer.Serialize(new { api, portal, partner, bffKey, accounts, socialToken, partnerSecret }));
_ = Task.Run(async () => { if (await Console.In.ReadLineAsync() == "stop") app.Lifetime.StopApplication(); });
try {
    await app.StartAsync(); Console.WriteLine("Auth bridge fixture: " + directory); await app.WaitForShutdownAsync();
} finally {
    await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var evidence = new { mode = "Production host; real isolated PostgreSQL; Bridge only", accounts = await db.Accounts.IgnoreQueryFilters().Where(x => x.IsSandbox).Select(x => new { x.Id, x.Email }).ToArrayAsync(),
        sessions = await db.Set<MiniAppBridgeSession>().Select(x => new { x.Id, x.AccountId, x.IsSandbox, x.ClientId, x.ConsumedAt, x.RevokedAt }).ToArrayAsync() };
    await File.WriteAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), ".codex-tmp", "auth-bridge-database.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Directory.Delete(directory, true);
}
