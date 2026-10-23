using System.Security.Claims;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;

namespace viora_BE.Admin;

public static class MiniAppReviewAdminApi
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled) return;
        MiniAppModerationApi.MapEndpoints(endpoints);
        var apps = endpoints.MapGroup("/api/admin/mini-apps").RequireAuthorization(MiniAppReviewAuthorization.Read)
            .RequireRateLimiting("mini-app-review-read").AddEndpointFilter<DeveloperApiFilter>();
        apps.MapGet("", async (HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store"; Query(http, "search", "page", "pageSize", "view");
            return Results.Ok(await service.ListAdminAppsAsync(Admin(http), Source(http), http.Request.Query["search"].FirstOrDefault(),
                MiniAppVersionApi.Page(http, "page", 1, 100000), MiniAppVersionApi.Page(http, "pageSize", 20, 100), ct, http.Request.Query["view"].FirstOrDefault()));
        });
        apps.MapGet("/{appId}", async (string appId, HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store"; Query(http); return Results.Ok(await service.GetAdminAppAsync(Admin(http), Source(http), appId, ct));
        });
        var group = endpoints.MapGroup("/api/admin/mini-app-reviews").RequireAuthorization(MiniAppReviewAuthorization.Read)
            .RequireRateLimiting("mini-app-review-read").AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer"; return await next(context);
            });
        group.MapGet("", async (HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        {
            Query(http, "search", "status", "assignee", "sort", "page", "pageSize");
            Guid? assignee = null;
            if (http.Request.Query.TryGetValue("assignee", out var raw))
            {
                if (!Guid.TryParseExact(raw.ToString(), "D", out var parsed) || parsed == Guid.Empty) throw MiniAppInput.Invalid("Người được giao không hợp lệ.");
                assignee = parsed;
            }
            return Results.Ok(await service.ListAdminAsync(Admin(http), Source(http), new(http.Request.Query["search"].FirstOrDefault(),
                http.Request.Query["status"].FirstOrDefault(), assignee, http.Request.Query["sort"].FirstOrDefault(),
                MiniAppVersionApi.Page(http, "page", 1, 100000), MiniAppVersionApi.Page(http, "pageSize", 20, 100)), ct));
        });
        group.MapGet("/overview", async (HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        { Query(http); return Results.Ok(await service.OverviewAdminAsync(Admin(http), Source(http), ct)); });
        group.MapGet("/{reviewId:guid}", async (Guid reviewId, HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        { Query(http); return Results.Ok(await service.GetAdminAsync(Admin(http), Source(http), reviewId, ct)); });
        group.MapPost("/{reviewId:guid}/start", async (Guid reviewId, HttpContext http, MiniAppReviewDecisionService service, CancellationToken ct) =>
        {
            Query(http); return Results.Ok(await service.StartAsync(Admin(http), Source(http), reviewId,
                await MiniAppVersionApi.Read<MiniAppReviewCommand>(http, 1024, ct), ct));
        }).RequireAuthorization(MiniAppReviewAuthorization.Decide).RequireRateLimiting("mini-app-review-write");
        foreach (var route in new[] { "reject", "request-changes", "approve" })
            group.MapPost("/{reviewId:guid}/" + route, async (Guid reviewId, HttpContext http, MiniAppReviewDecisionService service, CancellationToken ct) =>
            {
                Query(http); var request = await MiniAppVersionApi.Read<MiniAppReviewDecisionRequest>(http, 16384, ct);
                return Results.Ok(route switch {
                    "reject" => await service.RejectAsync(Admin(http), Source(http), reviewId, request, ct),
                    "request-changes" => await service.RequestChangesAsync(Admin(http), Source(http), reviewId, request, ct),
                    _ => await service.ApproveAsync(Admin(http), Source(http), reviewId, request, ct) });
            }).RequireAuthorization(MiniAppReviewAuthorization.Decide).RequireRateLimiting("mini-app-review-write");
        group.MapPost("/{reviewId:guid}/assign", async (Guid reviewId, HttpContext http, MiniAppReviewDecisionService service, CancellationToken ct) =>
        {
            Query(http); return Results.Ok(await service.AssignAsync(Admin(http), Source(http), reviewId,
                await MiniAppVersionApi.Read<MiniAppReviewAssignmentRequest>(http, 16384, ct), ct));
        }).RequireAuthorization(MiniAppReviewAuthorization.Decide).RequireRateLimiting("mini-app-review-write");
    }
    internal static void Query(HttpContext http, params string[] allowed)
    {
        if (http.Request.QueryString.Value?.Length > 1024 || http.Request.Query.Any(x => !allowed.Contains(x.Key, StringComparer.Ordinal) || x.Value.Count != 1))
            throw MiniAppInput.Invalid("Tham số truy vấn không hợp lệ.");
    }
    internal static Guid Admin(HttpContext http) => Guid.Parse(http.User.FindFirstValue("sub")!);
    internal static Guid Source(HttpContext http) => Guid.Parse(http.User.FindFirstValue("sid")!);
}
