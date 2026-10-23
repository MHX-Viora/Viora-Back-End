using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppPublishingApi
{
    public static void MapEndpoints(RouteGroupBuilder developer)
    {
        if (!((IEndpointRouteBuilder)developer).ServiceProvider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled) return;
        developer.MapGet("/{appId}/publication-readiness", async (string appId, HttpContext http, MiniAppReadinessService service, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (http.Request.QueryString.Value?.Length > 128 || http.Request.Query.Count != 1 ||
                !http.Request.Query.TryGetValue("versionId", out var values) || values.Count != 1 ||
                !Guid.TryParseExact(values[0], "D", out var versionId) || versionId == Guid.Empty)
                throw MiniAppInput.Invalid("Phải chọn đúng một phiên bản đã duyệt.");
            return Results.Ok((await service.PublicationAsync(MiniAppReviewApi.Owner(http), MiniAppReviewApi.Source(http), appId, versionId, ct)).Readiness);
        }).RequireRateLimiting("mini-app-review-read");
        developer.MapGet("/{appId}/publications", async (string appId, HttpContext http, MiniAppPublishingService service, CancellationToken ct) =>
        {
            MiniAppReviewApi.Private(http, list: true);
            return Results.Ok(await service.ListAsync(MiniAppReviewApi.Owner(http), MiniAppReviewApi.Source(http), appId,
                MiniAppVersionApi.Page(http, "page", 1, 100000), MiniAppVersionApi.Page(http, "pageSize", 20, 100), ct));
        }).RequireRateLimiting("mini-app-review-read");
        developer.MapPost("/{appId}/publish", async (string appId, HttpContext http, MiniAppPublishingService service, CancellationToken ct) =>
        {
            MiniAppReviewApi.Private(http);
            return Results.Ok(await service.PublishAsync(MiniAppReviewApi.Owner(http), MiniAppReviewApi.Source(http), appId,
                await MiniAppVersionApi.Read<MiniAppPublishRequest>(http, 4096, ct), ct));
        }).RequireRateLimiting("mini-app-review-write");
        developer.MapPost("/{appId}/unpublish", async (string appId, HttpContext http, MiniAppPublishingService service, CancellationToken ct) =>
        {
            MiniAppReviewApi.Private(http);
            return Results.Ok(await service.UnpublishAsync(MiniAppReviewApi.Owner(http), MiniAppReviewApi.Source(http), appId,
                await MiniAppVersionApi.Read<MiniAppUnpublishRequest>(http, 4096, ct), ct));
        }).RequireRateLimiting("mini-app-review-write");
    }
}
