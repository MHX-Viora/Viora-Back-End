using System.Globalization;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppPublicApi
{
    public static void MapMiniAppPublicApi(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value.Enabled &&
            !endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppBridgeOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/api/mini-apps").AllowAnonymous()
            .RequireRateLimiting("mini-app-public-read").AddEndpointFilter<MiniAppProductionFilter>();
        group.MapGet("", async (HttpContext http, MiniAppPublicCatalogService service, CancellationToken ct) =>
        {
            Query(http, list: true);
            return Results.Ok(await service.ListAsync(new(Page(http, "page", 1), Page(http, "pageSize", 20)), ct));
        });
        group.MapGet("/{appId}", async (string appId, HttpContext http, MiniAppPublicCatalogService service, CancellationToken ct) =>
        {
            Query(http, list: false);
            if (appId.Length != 47 || !appId.StartsWith("app_", StringComparison.Ordinal) ||
                !appId.Skip(4).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                throw new DeveloperAuthException(404, "APP_NOT_FOUND", "Mini App hiện không khả dụng.");
            return Results.Ok(await service.DetailAsync(appId, ct));
        });
    }

    private static void Query(HttpContext http, bool list)
    {
        if (http.Request.QueryString.Value?.Length > 128 || http.Request.Query.Any(x => !list ||
            x.Key is not ("page" or "pageSize") || x.Value.Count != 1)) throw InvalidQuery();
    }

    private static int Page(HttpContext http, string key, int fallback) => !http.Request.Query.TryGetValue(key, out var value)
        ? fallback : int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var page) ? page : throw InvalidQuery();
    private static DeveloperAuthException InvalidQuery() => new(400, "INVALID_CATALOG_QUERY", "Tham số truy vấn không hợp lệ.");
}

internal sealed class MiniAppProductionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { return await next(context); }
        catch (DeveloperAuthException error) { return Results.Json(new { error = new { code = error.Code, message = error.Message } }, statusCode: error.Status); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            http.RequestServices.GetRequiredService<ILogger<MiniAppProductionFilter>>()
                .LogError("Mini App catalog request failed. TraceId: {TraceId}", http.TraceIdentifier);
            return Results.Json(new { error = new { code = "CATALOG_UNAVAILABLE", message = "Mini App tạm thời không khả dụng." } }, statusCode: 503);
        }
    }
}
