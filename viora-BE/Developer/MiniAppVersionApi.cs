using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppVersionApi
{
    public static void MapEndpoints(RouteGroupBuilder developer)
    {
        if (!((IEndpointRouteBuilder)developer).ServiceProvider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled) return;
        var group = developer.MapGroup("/{appId}/versions").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
            var list = http.Request.Method == "GET" && !http.Request.RouteValues.ContainsKey("versionId");
            if (http.Request.QueryString.Value?.Length > 128 || http.Request.Query.Any(x => !list ||
                x.Key is not ("page" or "pageSize") || x.Value.Count != 1)) throw MiniAppInput.Invalid("Tham số truy vấn không hợp lệ.");
            return await next(context);
        });
        group.MapGet("", async (string appId, HttpContext http, MiniAppVersionService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(Owner(http), Source(http), appId, Page(http, "page", 1, 100000), Page(http, "pageSize", 20, 100), ct)))
            .RequireRateLimiting("mini-app-review-read");
        group.MapGet("/{versionId:guid}", async (string appId, Guid versionId, HttpContext http, MiniAppVersionService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(Owner(http), Source(http), appId, versionId, ct))).RequireRateLimiting("mini-app-review-read");
        group.MapPost("", async (string appId, HttpContext http, MiniAppVersionService service, CancellationToken ct) =>
        {
            var request = await Read<MiniAppVersionCreateRequest>(http, 1024, ct);
            var result = await service.CreateAsync(Owner(http), Source(http), appId, request, ct);
            return Results.Created($"/api/developer/mini-apps/{Uri.EscapeDataString(appId)}/versions/{result.Id}", result);
        }).RequireRateLimiting("mini-app-review-write");
        group.MapPut("/{versionId:guid}", async (string appId, Guid versionId, HttpContext http, MiniAppVersionService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateDraftAsync(Owner(http), Source(http), appId, versionId,
                await Read<MiniAppVersionUpdateRequest>(http, 16384, ct), ct))).RequireRateLimiting("mini-app-review-write");
        group.MapPost("/{versionId:guid}/production-credentials", async (string appId, Guid versionId, HttpContext http,
            MiniAppVersionCredentials service, CancellationToken ct) => Results.Ok(await service.PrepareAsync(Owner(http), Source(http), appId,
                versionId, await Read<MiniAppVersionCommand>(http, 1024, ct), ct))).RequireRateLimiting("mini-app-review-credentials");
    }

    internal static async Task<T> Read<T>(HttpContext http, int maximum, CancellationToken ct)
    {
        try { return await MiniAppSandboxApi.ReadRequest<T>(http, ct, maximum); }
        catch (DeveloperAuthException e) when (e.Status == 413) { throw new DeveloperAuthException(413, "VERSION_REQUEST_TOO_LARGE", "Yêu cầu phiên bản vượt giới hạn."); }
    }
    internal static int Page(HttpContext http, string name, int fallback, int maximum)
    {
        if (!http.Request.Query.TryGetValue(name, out var raw)) return fallback;
        var value = raw.ToString();
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1 || parsed > maximum || value != parsed.ToString(CultureInfo.InvariantCulture))
            throw MiniAppInput.Invalid("Phân trang không hợp lệ.");
        return parsed;
    }
    private static Guid Owner(HttpContext http) => Guid.Parse(http.User.FindFirstValue("developer_id")!);
    private static Guid Source(HttpContext http) => Guid.Parse(http.User.FindFirstValue("developer_session")!);
}
