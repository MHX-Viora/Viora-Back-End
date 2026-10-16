using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Viora.Infrastructure.Security;
using viora_BE.Sso;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

public sealed class SsoBootstrapTests
{
    [Fact]
    public async Task PostgreSqlBootstrapMigrationAndConcurrentCompletionIssueOnlyOneSession()
    {
        await using var pg = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var previous = db.Database.GetMigrations().Single(x => x.EndsWith("_AddAnktSsoSessions"));
        var current = db.Database.GetMigrations().Single(x => x.EndsWith("_AddSsoLoginTransactions"));
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"SsoLoginTransactions\"; CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
        var migrator = db.GetService<IMigrator>(); await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        var account = new Account { Email = "bootstrap-pg@example.test" }; var social = Guid.NewGuid();
        db.Accounts.Add(account); db.RefreshTokens.Add(new RefreshToken { AccountId = account.Id, SessionId = social, TokenHash = "bootstrap-test-pg", ExpiresAt = DateTime.UtcNow.AddHours(2) }); await db.SaveChangesAsync();
        var bootstrap = new SsoBootstrapService(db, new SsoSessionService(db, TimeProvider.System), TimeProvider.System);
        var start = await bootstrap.StartAsync("/connect/authorize?client_id=test", default);
        await bootstrap.AuthenticateAsync(start.TransactionId, account.Id, social, default);
        async Task<bool> Complete()
        {
            await using var other = new AppDbContext(options);
            var service = new SsoBootstrapService(other, new(other, TimeProvider.System), TimeProvider.System);
            try { await service.CompleteAsync(start.TransactionId, start.BrowserCredential, default); return true; }
            catch (DeveloperAuthException e) when (e.Code == "SSO_TRANSACTION_INVALID") { return false; }
        }
        var results = await Task.WhenAll(Complete(), Complete());
        Assert.Single(results, x => x); Assert.Equal(1, await db.AnktSsoSessions.CountAsync());
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Equal(1, await db.Accounts.CountAsync()); Assert.Equal(1, await db.AnktSsoSessions.CountAsync());
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
    }
    [Fact]
    public async Task RealJwtBootstrapEnforcesOriginBrowserCookieAndTopLevelCompletion()
    {
        await using var f = await Fixture.Create();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(f.Bootstrap); builder.Services.AddSingleton(f.Sessions);
        builder.Services.Configure<SsoOptions>(x => { x.Issuer = "https://issuer.example"; x.FirstPartyOrigin = "https://app.example"; });
        var key = new string('q', 40);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x =>
        {
            x.MapInboundClaims = false;
            x.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = "viora-BE", ValidAudience = "viora-client",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidAlgorithms = [SecurityAlgorithms.HmacSha256] };
        });
        builder.Services.AddAuthorization(); builder.Services.AddCors(x => x.AddPolicy("SsoBootstrap", p => p.WithOrigins("https://app.example").WithMethods("POST").WithHeaders("Authorization", "Content-Type")));
        builder.Services.AddRateLimiter(x => x.AddPolicy("sso", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        await using var app = builder.Build(); app.UseCors(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
        app.MapSsoSessionEndpoints(); await app.StartAsync();
        var client = app.GetTestClient(); client.BaseAddress = new("https://issuer.example");
        var start = await client.GetAsync("/sso/start?returnUrl=" + Uri.EscapeDataString("/connect/authorize?client_id=test"));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.StartsWith("https://app.example/sso/login?transaction=", start.Headers.Location!.AbsoluteUri);
        var transaction = Guid.Parse(QueryHelpers.ParseQuery(start.Headers.Location.Query)["transaction"].ToString());
        var binding = Assert.Single(start.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", binding, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", binding, StringComparison.OrdinalIgnoreCase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtTokenService(Options.Create(new JwtOptions { Key = key })).CreateTokens(f.Account, f.SocialId).Tokens.AccessToken);
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/sso/bootstrap", new { transactionId = transaction })).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://app.example");
        var authenticated = await client.PostAsJsonAsync("/sso/bootstrap", new { transactionId = transaction });
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.DoesNotContain("accessToken", await authenticated.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = null;
        var url = "/sso/complete?transaction=" + transaction;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", binding.Split(';')[0]);
        var complete = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode); Assert.Equal("/connect/authorize?client_id=test", complete.Headers.Location!.OriginalString);
        var sessionCookie = complete.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(SsoSessionHandler.CookieName + "="));
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
        await app.StopAsync();
    }
    [Theory]
    [InlineData("https://evil.test")]
    [InlineData("//evil.test")]
    [InlineData("/\\evil.test")]
    [InlineData("/api/accounts/logout")]
    public async Task OnlyLocalAuthorizationReturnsAreAccepted(string returnUrl)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Bootstrap.StartAsync(returnUrl, default));
    }

    [Fact]
    public async Task BootstrapRequiresLiveSessionCorrectBrowserAndSingleUseCompletion()
    {
        await using var f = await Fixture.Create();
        var start = await f.Bootstrap.StartAsync("/connect/authorize?client_id=test", default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Bootstrap.CompleteAsync(start.TransactionId, start.BrowserCredential, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Bootstrap.AuthenticateAsync(start.TransactionId, f.Account.Id, Guid.NewGuid(), default));
        await f.Bootstrap.AuthenticateAsync(start.TransactionId, f.Account.Id, f.SocialId, default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Bootstrap.CompleteAsync(start.TransactionId, "wrong-browser", default));
        var result = await f.Bootstrap.CompleteAsync(start.TransactionId, start.BrowserCredential, default);
        Assert.Equal("/connect/authorize?client_id=test", result.ReturnUrl);
        Assert.Equal(f.Account.Id, (await f.Sessions.AuthenticateAsync(result.Session.Credential, default))!.AccountId);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Bootstrap.CompleteAsync(start.TransactionId, start.BrowserCredential, default));
    }

    [Fact]
    public async Task SocialSessionRevocationBetweenBootstrapAndCompletionFailsClosed()
    {
        await using var f = await Fixture.Create();
        var start = await f.Bootstrap.StartAsync("/connect/authorize?client_id=test", default);
        await f.Bootstrap.AuthenticateAsync(start.TransactionId, f.Account.Id, f.SocialId, default);
        await f.Db.RefreshTokens.ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Bootstrap.CompleteAsync(start.TransactionId, start.BrowserCredential, default));
        Assert.Empty(await f.Db.AnktSsoSessions.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("DataSource=:memory:");
        public AppDbContext Db = null!;
        public Account Account { get; } = new() { Email = "bootstrap@example.test" };
        public Guid SocialId { get; } = Guid.NewGuid();
        public SsoBootstrapService Bootstrap = null!;
        public SsoSessionService Sessions = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.connection.OpenAsync(); f.connection.CreateFunction<string?, int>("char_length", x => x?.Length ?? 0);
            f.Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.connection).Options); await f.Db.Database.EnsureCreatedAsync();
            f.Db.Accounts.Add(f.Account); f.Db.RefreshTokens.Add(new RefreshToken { AccountId = f.Account.Id, SessionId = f.SocialId, TokenHash = "test-bootstrap", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            await f.Db.SaveChangesAsync();
            f.Sessions = new(f.Db, TimeProvider.System); f.Bootstrap = new(f.Db, f.Sessions, TimeProvider.System);
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
