using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

namespace viora_BE.Developer;

public static class MiniAppApi
{
    public static IServiceCollection AddDeveloperMiniApps(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MiniAppOptions>(config.GetSection("MiniApps"));
        services.Configure<Viora.Infrastructure.Media.CloudinaryOptions>(config.GetSection("Cloudinary"));
        services.AddScoped<MiniAppSecretProtector>(); services.AddScoped<MiniAppService>(); services.AddScoped<MiniAppLogoStorage>();
        services.AddScoped<MiniAppSsoService>();
        services.AddMiniAppReviewServices(config);
        MiniAppSandboxApi.AddServices(services,config);
        services.AddHostedService<MiniAppLogoCleanup>();
        MiniAppIntegrationApi.AddServices(services);
        services.AddRateLimiter(o => o.AddPolicy("mini-app-sensitive", h => RateLimitPartition.GetFixedWindowLimiter(
            h.User.FindFirstValue("developer_id") ?? h.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }
    public static IEndpointRouteBuilder MapDeveloperMiniApps(this IEndpointRouteBuilder endpoints)
    {
        viora_BE.Admin.MiniAppReviewAdminApi.MapEndpoints(endpoints);
        MiniAppReviewNotificationApi.MapEndpoints(endpoints);
        MiniAppSandboxApi.MapAdminEndpoints(endpoints);
        MiniAppSandboxRuntimeApi.MapEndpoints(endpoints);
        MiniAppSandboxHostApi.MapEndpoints(endpoints);
        MiniAppSandboxExecutorApi.MapEndpoints(endpoints);
        var g = endpoints.MapGroup("/api/developer/mini-apps").WithTags("Developer Mini Apps")
            .RequireAuthorization(DeveloperSessionHandler.Policy).RequireRateLimiting("developer-auth")
            .AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>();
        MiniAppIntegrationApi.MapEndpoints(g);
        MiniAppVersionApi.MapEndpoints(g);
        MiniAppReviewApi.MapEndpoints(g);
        MiniAppSsoApi.MapEndpoints(g);
        MiniAppSandboxApi.MapEndpoints(g);
        g.MapGet("", async (MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.List(Owner(h), int.TryParse(h.Request.Query["page"], out var page) ? page : 1, ct)));
        g.MapPost("", async (MiniAppCreateRequest r, MiniAppService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Create(Owner(h), r, ct))).RequireRateLimiting("mini-app-sensitive");
        g.MapGet("/{appId}", async (string appId, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Get(Owner(h), appId, ct)));
        g.MapPatch("/{appId}", async (string appId, MiniAppUpdateRequest r, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Update(Owner(h), appId, r, ct)));
        g.MapDelete("/{appId}", async (string appId, MiniAppService s, HttpContext h, CancellationToken ct) => { await s.Delete(Owner(h), appId, ct); return Results.NoContent(); });
        g.MapGet("/{appId}/credentials", async (string appId, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Credentials(Owner(h), appId, false, ct))).RequireRateLimiting("mini-app-sensitive");
        g.MapPost("/{appId}/credentials/rotate", async (string appId, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Credentials(Owner(h), appId, true, ct))).RequireRateLimiting("mini-app-sensitive");
        g.MapGet("/{appId}/redirect-uris", async (string appId, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Redirects(Owner(h), appId, ct)));
        g.MapPut("/{appId}/redirect-uris", async (string appId, MiniAppRedirectRequest r, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.SaveRedirects(Owner(h), appId, r, ct)));
        g.MapGet("/{appId}/activity", async (string appId, MiniAppService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Activity(Owner(h), appId, ct)));
        // Raw image upload avoids multipart form/cookie auth ambiguity. The same-origin BFF enforces CSRF.
        g.MapPost("/logo", async (HttpContext h, AppDbContext db, MiniAppLogoStorage storage, CancellationToken ct) =>
        {
            var mime = h.Request.ContentType?.Split(';')[0] ?? "";
            using var content = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await h.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (content.Length + count > MiniAppLogoStorage.MaxBytes) return Results.Json(new { error = new { code = "REQUEST_TOO_LARGE", message = "Logo tối đa 2 MB." } }, statusCode: 413);
                await content.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            var bytes = content.ToArray(); MiniAppLogoStorage.Validate(bytes, mime);
            var upload = new MiniAppLogoUpload { DeveloperAccountId = Owner(h), ExpiresAt = DateTime.UtcNow.AddHours(1), PublicId = "developer-mini-app-logos/" + Guid.NewGuid().ToString("N") };
            db.MiniAppLogoUploads.Add(upload); await db.SaveChangesAsync(ct);
            upload.Url = await storage.Upload(bytes, upload.PublicId, ct);
            await db.SaveChangesAsync(ct); return Results.Ok(new MiniAppLogoDto(upload.Id, upload.Url));
        }).RequireRateLimiting("mini-app-sensitive");
        return endpoints;
    }
    private static Guid Owner(HttpContext h) => Guid.Parse(h.User.FindFirstValue("developer_id")!);
}

public sealed class MiniAppConflictFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" or "40001" or "40P01" } || ex.InnerException?.GetType().Name == "SqliteException")
        { return Conflict(); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001" || ex.SqlState == "40P01") { return Conflict(); }
        catch (InvalidOperationException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "40001" or "40P01" }) { return Conflict(); }
    }
    private static IResult Conflict() => Results.Json(new { error = new { code = "RETRY_REQUIRED", message = "Dữ liệu vừa thay đổi. Vui lòng thử lại với cùng yêu cầu." } }, statusCode: 409);
}
