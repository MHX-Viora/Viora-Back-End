using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Application.MiniApps;
using Viora.Infrastructure.Persistence;
using viora_BE.Controllers.Admin;
using viora_BE.Controllers.Developer;
using viora_BE.Controllers.OpenApi;
using viora_BE.OpenApi;
using Xunit;

public sealed class OpenApiManagementTests
{
    [Fact]
    public async Task ActualMvcRevokeEndpointRequiresAdminAndValidReason()
    {
        await using var fixture = await Fixture.Create();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(fixture.Db); builder.Services.AddSingleton(new PartnerOpenApiOptions());
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminOpenApiController).Assembly);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, RoleHandler>("fixture", _ => { }); builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        var client = app.GetTestClient(); var path = $"/api/admin/mini-apps/{fixture.AppId}/open-api/revoke-tokens";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { reason = "Security incident" })).StatusCode);
        client.DefaultRequestHeaders.Add("Fixture-Role", "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { reason = "Security incident" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Fixture-Role"); client.DefaultRequestHeaders.Add("Fixture-Role", "2");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new { reason = "Security incident" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { reason = "   " })).StatusCode);
    }
    [Fact]
    public async Task DeveloperMetadataRejectsOtherOwnersAndIncludesOnlyApprovedActiveScopes()
    {
        await using var fixture = await Fixture.Create();
        var controller = new DeveloperOpenApiController(fixture.Db, new() { Enabled = true, Issuer = "https://openapi.example" });
        controller.ControllerContext = Context(fixture.Owner);
        var response = Assert.IsType<OkObjectResult>((await controller.Get(fixture.AppId, default)).Result);
        var metadata = Assert.IsType<OpenApiManagementView>(response.Value);
        Assert.Equal(new[] { "openid" }, metadata.ApprovedScopes);
        Assert.Equal("https://openapi.example/.well-known/openid-configuration", metadata.DiscoveryUrl);
        Assert.DoesNotContain("synthetic-secret-hash", JsonSerializer.Serialize(metadata));
        controller.ControllerContext = Context(Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await controller.Get(fixture.AppId, default)).Result);
    }

    [Fact]
    public async Task DisabledFeatureMetadataIsSafeAndRevokeDoesNotResolveProtocolServices()
    {
        await using var fixture = await Fixture.Create();
        var controller = new AdminOpenApiController(fixture.Db, new());
        var result = Assert.IsType<OkObjectResult>((await controller.Get(fixture.AppId, default)).Result);
        var metadata = Assert.IsType<OpenApiManagementView>(result.Value);
        Assert.False(metadata.Enabled); Assert.False(metadata.Eligible); Assert.Null(metadata.Issuer);
        Assert.IsType<ConflictObjectResult>(await controller.Revoke(fixture.AppId, new("Security incident"), default));
    }

    [Fact]
    public async Task IntegrationDisplaysPublishedCallbackInsteadOfUnsavedDraftCallback()
    {
        await using var fixture = await Fixture.Create();
        var app = await fixture.Db.MiniApps.SingleAsync(a => a.Id == fixture.AppId);
        app.CallbackUrls = new[] { "https://draft.example/unapproved" };
        app.PublishedConfigurationJson = JsonSerializer.Serialize(new MiniAppConfigurationInput("Published", "published", null, null, null, "https://partner.example", "https://partner.example/callback", new[] { "partner.example" }, new[] { "identity.login" }, AuthenticationMode: "AnktSso", CallbackUrls: new[] { "https://partner.example/callback" }));
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.MiniAppPermissionMappings.Where(m => m.MiniAppId == fixture.AppId).ExecuteDeleteAsync();
        fixture.Db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>((await new AdminOpenApiController(fixture.Db, new() { Enabled = true, Issuer = "https://openapi.example" }).Get(fixture.AppId, default)).Result);
        var view = Assert.IsType<OpenApiManagementView>(result.Value);
        Assert.Equal(new[] { "https://partner.example/callback" }, view.ApprovedCallbackUrls);
        Assert.Contains("openid", view.ApprovedScopes);
    }

    [Fact]
    public async Task RecentErrorsExcludeForeignAppsAndMalformedRecordsAndRedactUrlQueries()
    {
        await using var fixture = await Fixture.Create();
        foreach (var detail in new[] { "[]", "42", "null", "{\"status\":\"401\"}", "{\"status\":[]} " })
            fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = fixture.AppId, Action = "OpenApiRequest", Detail = detail });
        fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = fixture.AppId, Action = "OpenApiRequest", Detail = JsonSerializer.Serialize(new { status = 403, path = "/v1/me/profile?code=SECRET#fragment", correlationId = "safe_trace-123", body = "PRIVATE" }) });
        fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = fixture.AppId, Action = "OpenApiRequest", Detail = JsonSerializer.Serialize(new { status = 401, path = "/oauth/token#PRIVATE", correlationId = "unsafe.token.value" }) });
        fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = fixture.AppId, Action = "OpenApiRequest", Detail = JsonSerializer.Serialize(new { status = 500, path = "/api/admin/secret?code=SECRET", correlationId = "hidden" }) });
        // Global/unattributed failures must not appear in a particular developer's history.
        fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = null, Action = "OpenApiRequest", Detail = JsonSerializer.Serialize(new { status = 401, path = "/oauth/token", correlationId = "another-app" }) });
        await fixture.Db.SaveChangesAsync();
        var controller = new DeveloperOpenApiController(fixture.Db, new()) { ControllerContext = Context(fixture.Owner) };
        var result = Assert.IsType<OkObjectResult>((await controller.Get(fixture.AppId, default)).Result);
        var errors = Assert.IsType<OpenApiManagementView>(result.Value).RecentErrors!;
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Status == 403 && e.Path == "/v1/me/profile" && e.CorrelationId == "safe_trace-123");
        Assert.Contains(errors, e => e.Status == 401 && e.Path == "/oauth/token" && e.CorrelationId == null);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(errors)); Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(errors));
        Assert.DoesNotContain("another-app", JsonSerializer.Serialize(errors));
    }

    [Fact]
    public async Task UsageAggregatesSanitizedStatusesAndToleratesOldInvalidAuditRecords()
    {
        await using var fixture = await Fixture.Create();
        foreach (var status in new[] { 200, 302, 401, 403, 429 }) fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = fixture.AppId, Action = "OpenApiRequest", Detail = JsonSerializer.Serialize(new { status }) });
        fixture.Db.MiniAppAuditLogs.Add(new() { MiniAppId = fixture.AppId, Action = "OpenApiRequest", Detail = "old invalid log" });
        await fixture.Db.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>((await new AdminOpenApiController(fixture.Db, new()).Get(fixture.AppId, default)).Result);
        var usage = Assert.IsType<OpenApiManagementView>(result.Value).Usage;
        Assert.Equal(5, usage.Requests); Assert.Equal(2, usage.Successful);
        Assert.Equal(1, usage.Unauthorized); Assert.Equal(1, usage.Forbidden); Assert.Equal(1, usage.RateLimited);
    }

    private static ControllerContext Context(Guid accountId) => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", accountId.ToString()) }, "fixture")) } };
    private sealed class RoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["Fixture-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection; public required AppDbContext Db; public required Guid Owner; public required Guid AppId;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            connection.CreateFunction("char_length", (string value) => value.Length);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var owner = Guid.NewGuid(); var appId = Guid.NewGuid(); var developer = new Developer { AccountId = owner, Name = "Fixture developer", Email = "developer@example.com", Status = DeveloperStatus.Active };
            var app = new MiniApp { Id = appId, Developer = developer, Name = "Fixture app", Slug = "fixture", WebUrl = "https://partner.example", CallbackUrl = "https://partner.example/callback", ClientId = "fixture-client", ClientSecretHash = "synthetic-secret-hash", Status = MiniAppStatus.Active };
            db.AddRange(new Account { Id = owner, Email = "owner@example.com" }, app);
            foreach (var (code, status) in new[] { ("identity.login", MiniAppPermissionStatus.Active), ("profile.email", MiniAppPermissionStatus.Inactive) })
            { var permission = await db.MiniAppPermissions.SingleAsync(p => p.Code == code); permission.Status = status; db.Add(new MiniAppPermissionMapping { MiniApp = app, Permission = permission }); }
            await db.SaveChangesAsync(); return new() { Connection = connection, Db = db, Owner = owner, AppId = appId };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
