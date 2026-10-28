using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using viora_BE.Developer;
using Xunit;

public sealed class DeveloperApiTests
{
    private const string BffKey = "test-only-signing-material-not-a-production-secret";
    [Fact]
    public async Task RealHttpRegistrationVerificationLoginAndRevocation()
    {
        await using var host = await Host.Create(); var client = host.Client;
        var register = await client.PostAsJsonAsync("/api/developer/auth/register", new { fullName = "HTTP Developer", email = "http@example.test", password = "HTTP integration password 2026", confirmPassword = "HTTP integration password 2026", acceptTerms = true });
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        var verified = await client.PostAsJsonAsync("/api/developer/auth/verify-email", new { token = host.Mail.Token }); Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var response = await client.PostAsJsonAsync("/api/developer/auth/login", new { email = "http@example.test", password = "HTTP integration password 2026" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<DeveloperLoginResult>(); Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new(DeveloperSessionHandler.SchemeName, login.SessionToken);
        var profile = await client.GetAsync("/api/developer/profile"); Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.DoesNotContain("passwordHash", await profile.Content.ReadAsStringAsync());
        Assert.Equal("no-store", profile.Headers.CacheControl?.ToString());
        // Exhaust the anonymous bucket; authenticated account reads have an independent quota.
        for (var i = 0; i < 57; i++) await client.GetAsync("/api/developer/linked-accounts/ankt/callback");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/developer/linked-accounts/ankt/callback")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/developer/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/developer/auth/refresh", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/developer/auth/logout", new { })).StatusCode);
        // Revoked sessions become anonymous; this fixture has already exhausted that quota.
        Assert.Contains((await client.GetAsync("/api/developer/profile")).StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests });
    }

    [Fact]
    public async Task GuestAndAnktBearerCannotAccessDeveloperAccountApis()
    {
        await using var host = await Host.Create();
        foreach (var path in new[] { "auth/me", "profile", "sessions", "linked-accounts" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/developer/" + path)).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ankt-access-token-is-not-a-developer-session");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/developer/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await host.Client.PostAsJsonAsync("/api/developer/linked-accounts/ankt/start", new { })).StatusCode);
    }

    [Fact]
    public async Task InvalidInputAndIpRateLimitAreEnforced()
    {
        await using var host = await Host.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/developer/auth/register", new { fullName = "", email = "bad", password = "short", confirmPassword = "short" })).StatusCode);
        for (var i = 0; i < 59; i++) Assert.Equal(HttpStatusCode.NotImplemented, (await host.Client.GetAsync("/api/developer/linked-accounts/ankt/callback")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.GetAsync("/api/developer/linked-accounts/ankt/callback")).StatusCode);
        // A signed second client behind the same BFF must receive its own bucket.
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        host.Client.DefaultRequestHeaders.Add("X-ANKT-Bff-IP", "203.0.113.8");
        host.Client.DefaultRequestHeaders.Add("X-ANKT-Bff-Time", time);
        host.Client.DefaultRequestHeaders.Add("X-ANKT-Bff-Signature", Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(BffKey), System.Text.Encoding.UTF8.GetBytes($"203.0.113.8\n{time}\nGET\n/api/developer/linked-accounts/ankt/callback"))));
        Assert.Equal(HttpStatusCode.NotImplemented, (await host.Client.GetAsync("/api/developer/linked-accounts/ankt/callback")).StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-ANKT-Bff-Signature");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/api/developer/linked-accounts/ankt/callback")).StatusCode);
    }

    private sealed class Mail : IDeveloperEmailSender
    {
        public string Token = "";
        public bool IsConfigured => true;
        public Task SendAsync(string recipient, string subject, string text, CancellationToken ct) { Token = text.Split("#token=")[1].Split('\n')[0].Trim(); return Task.CompletedTask; }
    }
    private sealed class Host(WebApplication app, SqliteConnection connection, HttpClient client, Mail mail) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public Mail Mail => mail;
        public static async Task<Host> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
            var b = WebApplication.CreateBuilder(); b.WebHost.UseTestServer();
            b.Logging.ClearProviders();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DeveloperAuth:PortalUrl"] = "https://mxh.ankt.vn/developers", ["DeveloperAuth:BffSharedSecret"] = BffKey });
            b.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection)); b.Services.AddDeveloperAccounts(b.Configuration);
            var mail = new Mail(); b.Services.AddSingleton<IDeveloperEmailSender>(mail);
            var app = b.Build();
            using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
            app.UseDeveloperAccountBoundary(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.MapDeveloperAccountEndpoints();
            await app.StartAsync(); return new(app, connection, app.GetTestClient(), mail);
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
