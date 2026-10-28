using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppProductionHostApi
{
    public const string BindingCookie = "__Host-AnktMiniAppBinding";
    public static string RuntimeCookie(Guid sessionId) => "__Host-AnktMiniAppRuntime-" + sessionId.ToString("N");
    public static void MapMiniAppProductionHostApi(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/api/mini-app-host").AllowAnonymous().AddEndpointFilter<MiniAppProductionHostFilter>();
        group.MapGet("/prepare", (HttpContext http, MiniAppProductionBindingService service) =>
        {
            var prepared = service.Prepare(UniqueCookie(http, BindingCookie));
            http.Response.Cookies.Append(BindingCookie, prepared.CookieCredential, new CookieOptions
            { Secure = true, HttpOnly = true, SameSite = SameSiteMode.None, Path = "/", Expires = new DateTimeOffset(prepared.CookieExpiresAt) });
            return Results.Ok(prepared.Public);
        }).RequireRateLimiting("mini-app-host-prepare");
        group.MapPost("/redeem", async (HttpContext http, MiniAppProductionGrantService service, CancellationToken ct) =>
        {
            var request = await MiniAppProductionRequest.ReadAsync<MiniAppRedeemRequest>(http, 1024, ct);
            var issued = await service.RedeemAsync(request.Grant, request.BrowserNonce, UniqueCookie(http, BindingCookie), ct);
            http.Response.Cookies.Append(RuntimeCookie(issued.Public.SessionId), issued.Credential, new CookieOptions
            { Secure = true, HttpOnly = true, SameSite = SameSiteMode.None, Path = "/", Expires = new DateTimeOffset(issued.Public.ExpiresAt) });
            return Results.Ok(issued.Public);
        }).RequireRateLimiting("mini-app-host-redeem");
    }
    internal static string? UniqueCookie(HttpContext http, string name)
    {
        var count = 0;
        foreach (var header in http.Request.Headers.Cookie)
        {
            if (header == null || header.Contains(',')) return null;
            foreach (var part in header.Split(';'))
                if (part.TrimStart().StartsWith(name + "=", StringComparison.Ordinal)) count++;
        }
        return count == 1 ? http.Request.Cookies[name] : null;
    }
}

internal sealed class MiniAppProductionHostFilter(MiniAppProductionOptions options, ILogger<MiniAppProductionHostFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        try
        {
            if (!http.Request.IsHttps || http.Request.Scheme + "://" + http.Request.Host.Value != options.HostOrigin ||
                (http.Request.Method != "GET" && http.Request.Headers.Origin.Count != 1) ||
                http.Request.Headers.Origin.Count > 1 || (http.Request.Headers.Origin.Count == 1 && http.Request.Headers.Origin.ToString() != options.HostOrigin))
                throw new DeveloperAuthException(403, "HOST_ORIGIN_DENIED", "Không thể sử dụng host từ nguồn này.");
            if (http.Request.QueryString.HasValue) throw new DeveloperAuthException(400, "INVALID_HOST_QUERY", "Tham số host không hợp lệ.");
            return await next(context);
        }
        catch (DeveloperAuthException e) { return Results.Json(new { error = new { code = e.Code, message = e.Message } }, statusCode: e.Status); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogError("Mini App host request failed. TraceId: {TraceId}", http.TraceIdentifier);
            return Results.Json(new { error = new { code = "HOST_UNAVAILABLE", message = "Mini App tạm thời không khả dụng." } }, statusCode: 503);
        }
    }
}
