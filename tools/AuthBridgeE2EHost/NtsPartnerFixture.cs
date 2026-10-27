using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DigitalCreative.Api.Auth;
using DigitalCreative.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

// Test-only adapter for the real Nghệ Thuật Số gateway, account and role services.
// It never loads appsettings/.env or contacts a deployed database.
internal sealed class NtsPartnerFixture : IAsyncDisposable
{
    private readonly string connection;
    private readonly string api, origin, secret;
    private readonly HttpClient http;
    private readonly JwtOptions jwt = new() { Key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)) };
    private readonly EphemeralDataProtectionProvider protection = new();
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { Converters = { DigitalCreative.Api.Serialization.ApiJson.CreateEnumConverter() } };
    private NtsPartnerFixture(string connection, string api, string origin, string secret, X509Certificate2 cert)
    {
        this.connection = connection; this.api = api; this.origin = origin; this.secret = secret;
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = async (context, ct) => {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        }};
        var pin = cert.GetCertHashString();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == pin;
        http = new(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }
    public static async Task<NtsPartnerFixture> Create(string pg, string api, string origin, string secret, X509Certificate2 cert)
    {
        await using var admin = new NpgsqlConnection(pg); await admin.OpenAsync();
        await using var create = new NpgsqlCommand("CREATE DATABASE nts_bridge_sandbox", admin); await create.ExecuteNonQueryAsync();
        var fixture = new NtsPartnerFixture(new NpgsqlConnectionStringBuilder(pg) { Database = "nts_bridge_sandbox" }.ConnectionString, api, origin, secret, cert);
        await using var db = fixture.Database(); await db.Database.MigrateAsync();
        return fixture;
    }
    private ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
    public void Map(WebApplication app)
    {
        app.MapGet("/fixture/nts/evidence", async () => {
            await using var db = Database();
            return Results.Json(new { accounts = await db.Accounts.CountAsync(), links = await db.AnktIdentities.CountAsync() });
        });
        app.MapPost("/fixture/nts/{client}/exchange", async (string client, HttpContext context) => {
            var request = await context.Request.ReadFromJsonAsync<CodeRequest>();
            return await Run(client, async service => Results.Json(await service.LoginAsync(request!.Code, context.RequestAborted), json));
        });
        app.MapPost("/fixture/nts/{client}/select-role", async (string client, HttpContext context) => {
            var request = await context.Request.ReadFromJsonAsync<RoleRequest>();
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(context.Request.Headers.Authorization.ToString()[7..], JwtSecurity.CreateValidationParameters(jwt), out _);
            return await Run(client, async service => Results.Json(await service.SelectRoleAsync(principal, request!.Role, context.RequestAborted), json));
        });
    }
    private async Task<IResult> Run(string client, Func<AnktBridgeLoginService, Task<IResult>> action)
    {
        if (client is not ("app_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" or "app_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")) return Results.BadRequest();
        await using var db = Database();
        var settings = Options.Create(new AnktMiniAppOptions { Enabled = true, ClientId = client, ClientSecret = secret,
            ApiOrigin = api, MiniAppOrigin = origin, Environment = "Sandbox" });
        var jwtOptions = Options.Create(jwt); var clock = TimeProvider.System;
        var tokens = new TokenService(jwtOptions, clock); var sessions = new AccountSessionService(db, tokens, jwtOptions, clock);
        var accounts = new AnktMiniAppService(db, sessions, new AnktMiniAppGateway(http, settings), settings, protection, clock);
        try { return await action(new(db, accounts, new AnktPartnerGateway(http, settings, clock), tokens, sessions)); }
        catch (AuthException e) { return Results.Json(new { message = e.Message }, statusCode:e.StatusCode); }
    }
    private sealed record CodeRequest(string Code);
    private sealed record RoleRequest(string Role);
    public ValueTask DisposeAsync() { http.Dispose(); return ValueTask.CompletedTask; }
}
