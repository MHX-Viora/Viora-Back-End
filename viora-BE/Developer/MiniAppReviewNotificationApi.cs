using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppReviewNotificationApi
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/api/developer/review-notifications").RequireAuthorization(DeveloperSessionHandler.Policy)
            .AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>().AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer"; return await next(context);
            });
        group.MapGet("", async (HttpContext http, MiniAppReviewNotificationService service, CancellationToken ct) =>
        {
            if (http.Request.QueryString.Value?.Length > 128 || http.Request.Query.Any(x => x.Key is not ("page" or "pageSize" or "unread") || x.Value.Count != 1) ||
                http.Request.Query.TryGetValue("unread", out var value) && value[0] is not ("true" or "false")) throw MiniAppInput.Invalid("Tham số inbox không hợp lệ.");
            return Results.Ok(await service.ListAsync(MiniAppReviewApi.Owner(http), MiniAppReviewApi.Source(http), MiniAppVersionApi.Page(http, "page", 1, 100000),
                MiniAppVersionApi.Page(http, "pageSize", 20, 100), http.Request.Query["unread"] == "true", ct));
        }).RequireRateLimiting("mini-app-review-read");
        group.MapPost("/{id:guid}/read", async (Guid id, HttpContext http, MiniAppReviewNotificationService service, CancellationToken ct) =>
        {
            MiniAppReviewApi.Private(http); await MiniAppVersionApi.Read<MiniAppReviewNotificationReadRequest>(http, 1024, ct);
            return Results.Ok(await service.MarkReadAsync(MiniAppReviewApi.Owner(http), MiniAppReviewApi.Source(http), id, ct));
        }).RequireRateLimiting("mini-app-review-write");
    }
}
