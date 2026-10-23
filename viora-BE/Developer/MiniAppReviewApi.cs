using System.Security.Claims;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppReviewApi
{
    public static void MapEndpoints(RouteGroupBuilder developer)
    {
        if (!((IEndpointRouteBuilder)developer).ServiceProvider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled) return;
        MiniAppPublishingApi.MapEndpoints(developer);
        developer.MapGet("/{appId}/review-readiness", async (string appId, HttpContext http, MiniAppReadinessService service, CancellationToken ct) =>
        {
            Private(http);
            return Results.Ok((await service.PrepareAsync(Owner(http), Source(http), appId, null, ct)).Readiness);
        }).RequireRateLimiting("mini-app-review-read");
        developer.MapPost("/{appId}/reviews", async (string appId, HttpContext http, MiniAppReviewSubmissionService service, CancellationToken ct) =>
        {
            Private(http);
            var result = await service.SubmitAsync(Owner(http), Source(http), appId, await MiniAppVersionApi.Read<MiniAppReviewSubmitRequest>(http, 1024, ct), ct);
            return Results.Created($"/api/developer/mini-apps/{Uri.EscapeDataString(appId)}/reviews/{result.Id}", result);
        }).RequireRateLimiting("mini-app-review-write");
        developer.MapGet("/{appId}/reviews", async (string appId, HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        {
            Private(http, list: true);
            return Results.Ok(await service.ListOwnedAsync(Owner(http), Source(http), appId, MiniAppVersionApi.Page(http, "page", 1, 100000),
                MiniAppVersionApi.Page(http, "pageSize", 20, 100), ct));
        }).RequireRateLimiting("mini-app-review-read");
        developer.MapGet("/{appId}/reviews/{reviewId:guid}", async (string appId, Guid reviewId, HttpContext http, MiniAppReviewService service, CancellationToken ct) =>
        {
            Private(http); return Results.Ok(await service.GetOwnedAsync(Owner(http), Source(http), appId, reviewId, ct));
        }).RequireRateLimiting("mini-app-review-read");
    }
    internal static void Private(HttpContext http, bool list = false)
    {
        http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (http.Request.QueryString.Value?.Length > 128 || http.Request.Query.Any(x => !list || x.Key is not ("page" or "pageSize") || x.Value.Count != 1))
            throw MiniAppInput.Invalid("Tham số truy vấn không hợp lệ.");
    }
    internal static Guid Owner(HttpContext http) => Guid.Parse(http.User.FindFirstValue("developer_id")!);
    internal static Guid Source(HttpContext http) => Guid.Parse(http.User.FindFirstValue("developer_session")!);
}
