using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppPartnerLoginTests
{
    private const string Client = "app_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Origin = "https://partner.example.com", Secret = "partner-test-secret-with-at-least-32-bytes";
    private static void Partner(MiniAppAuthBridgeTests.Host h, string secret = Secret, string client = Client)
    {
        h.Http.DefaultRequestHeaders.Remove("Origin");
        h.Http.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client + ":" + secret)));
    }
    private static async Task Configure(MiniAppAuthBridgeTests.Host h)
    {
        // Bind through configuration to exercise the same registration as Render.
        await h.Scope(async s => {
            var configuration = s.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            configuration["MiniAppAuth:Partners:Nts:ClientId"] = Client;
            configuration["MiniAppAuth:Partners:Nts:SandboxSecretHash"] = MiniAppBridgeService.Hash(Secret);
            configuration["MiniAppAuth:Partners:Nts:ProductionSecretHash"] = MiniAppBridgeService.Hash("different-production-secret-at-least-32-bytes");
            s.GetRequiredService<IOptionsMonitorCache<MiniAppBridgeOptions>>().Clear();
            await s.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(s.GetRequiredService<IOptions<MiniAppBridgeOptions>>().Value, default);
            return true;
        });
    }
    private static async Task<JsonElement> Code(MiniAppAuthBridgeTests.Host h)
    {
        var token = await h.Exchange(await h.Launch());
        h.Http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        h.Http.DefaultRequestHeaders.Remove("Origin"); h.Http.DefaultRequestHeaders.Add("Origin", Origin);
        h.Http.DefaultRequestHeaders.Remove("X-Ankt-MiniApp-Client"); h.Http.DefaultRequestHeaders.Add("X-Ankt-MiniApp-Client", Client);
        var response = await h.Http.PostAsJsonAsync("/api/miniapp/auth/code", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var code = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(60, code.GetProperty("expiresIn").GetInt32());
        return code;
    }
    [Fact] public async Task PartnerExchangeCreatesVerifiedIdentityAndRejectsReplay()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create(); await Configure(h);
        var code = await Code(h); Partner(h);
        var response = await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange", new { code = code.GetProperty("code").GetString() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Client, identity.GetProperty("miniAppId").GetString());
        Assert.Equal("Sandbox", identity.GetProperty("environment").GetString());
        Assert.False(string.IsNullOrEmpty(identity.GetProperty("user").GetProperty("id").GetString()));
        Assert.False(identity.TryGetProperty("accessToken", out _));
        var replay = await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange", new { code = code.GetProperty("code").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var validation = await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/session", new { sessionId = identity.GetProperty("sessionId").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        await h.Scope(async s => { await s.GetRequiredService<AppDbContext>().DeveloperSessions.ExecuteUpdateAsync(x => x.SetProperty(r => r.RevokedAt, DateTime.UtcNow)); return true; });
        validation = await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/session", new { sessionId = identity.GetProperty("sessionId").GetGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, validation.StatusCode);
    }
    [Theory]
    [InlineData("expired", 400)] [InlineData("secret", 401)] [InlineData("production-secret", 401)]
    [InlineData("client", 401)] [InlineData("browser", 403)] [InlineData("source", 401)]
    [InlineData("parent", 401)] [InlineData("app", 403)]
    public async Task ExchangeRejectsInvalidBindingsWithoutConsumingCode(string change, int status)
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create(); await Configure(h);
        var code = await Code(h); Partner(h, change == "secret" ? "wrong" : change == "production-secret" ? "different-production-secret-at-least-32-bytes" : Secret,
            change == "client" ? "app_" + new string('b', 43) : Client);
        if (change == "browser") h.Http.DefaultRequestHeaders.Add("Origin", Origin);
        await h.Scope(async s => {
            var db = s.GetRequiredService<AppDbContext>();
            if (change == "expired") await db.Set<MiniAppBridgeSession>().ExecuteUpdateAsync(x => x.SetProperty(r => r.CodeExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
            if (change == "source") await db.DeveloperSessions.ExecuteUpdateAsync(x => x.SetProperty(r => r.RevokedAt, DateTime.UtcNow));
            if (change == "parent") await db.Set<MiniAppBridgeSession>().Where(x => x.CodeChallenge.Length == 43).ExecuteUpdateAsync(x => x.SetProperty(r => r.RevokedAt, DateTime.UtcNow));
            if (change == "app") await db.MiniApps.ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, MiniAppStatus.Suspended));
            return true;
        });
        var response = await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange", new { code = code.GetProperty("code").GetString() });
        Assert.Equal(status, (int)response.StatusCode);
        if (change is "secret" or "production-secret" or "client" or "browser") {
            Partner(h);
            Assert.Equal(HttpStatusCode.OK, (await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange", new { code = code.GetProperty("code").GetString() })).StatusCode);
        }
    }
    [Fact] public async Task PkceCodeCannotBeUsedAsPartnerCode()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create(); await Configure(h);
        var launch = await h.Launch(); Partner(h);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange", new { code = launch.Code })).StatusCode);
        await h.Exchange(launch);
    }
    [Fact] public async Task PartnerCodeCannotBeUsedInPublicPkceExchange()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create(); await Configure(h);
        var code = await Code(h);
        var raw = code.GetProperty("code").GetString()!;
        var grantId = await h.Scope(async s => (await s.GetRequiredService<AppDbContext>().Set<MiniAppBridgeSession>()
            .AsNoTracking().SingleAsync(x => x.CodeHash == MiniAppBridgeService.Hash(raw))).Id);
        var response = await h.Http.PostAsJsonAsync("/api/miniapp/auth/exchange", new {
            client_id = Client, session_id = grantId, code = raw,
            code_verifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Partner(h);
        Assert.Equal(HttpStatusCode.OK,(await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange",new {code=raw})).StatusCode);
    }
    [Fact] public async Task PostgreSqlConcurrentPartnerExchangeHasOnlyOneWinner()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var h = await MiniAppAuthBridgeTests.Host.Create(pg); await Configure(h);
        var code = await Code(h); Partner(h);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => h.Http.PostAsJsonAsync(
            "/api/miniapp/auth/partner/exchange", new { code = code.GetProperty("code").GetString() })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
    }
    [Fact] public async Task Partner_rate_limit_is_configurable_and_does_not_exhaust_host_requests()
    {
        await using var h = await MiniAppAuthBridgeTests.Host.Create(partnerRateLimit:2);
        Partner(h);
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.BadRequest,(await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange",new {code="bad"})).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await h.Http.PostAsJsonAsync("/api/miniapp/auth/partner/exchange",new {code="bad"})).StatusCode);
        var host = await h.Http.GetAsync("/api/miniapp/auth/config?clientId="+Client);
        Assert.NotEqual(HttpStatusCode.TooManyRequests,host.StatusCode);
    }
}
