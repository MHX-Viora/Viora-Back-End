using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppProductionRuntimeApi
{
    public static void MapMiniAppProductionRuntimeApi(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/api/mini-app-host").AllowAnonymous().RequireRateLimiting("mini-app-host-runtime")
            .AddEndpointFilter<MiniAppProductionHostFilter>();
        group.MapGet("/context", async (HttpContext http, MiniAppProductionRuntimeService service, CancellationToken ct) =>
        {
            var auth = Credentials(http);
            return Results.Ok(await service.ContextAsync(auth.Id, auth.Credential, auth.Nonce, auth.Browser, ct));
        });
        group.MapPost("/heartbeat", async (HttpContext http, MiniAppProductionRuntimeService service, CancellationToken ct) =>
        {
            var auth = Credentials(http); await MiniAppProductionRequest.ReadAsync<MiniAppHostActionRequest>(http, 256, ct);
            await service.HeartbeatAsync(auth.Id, auth.Credential, auth.Nonce, auth.Browser, ct); return Results.NoContent();
        });
        group.MapPost("/close", async (HttpContext http, MiniAppProductionRuntimeService service, CancellationToken ct) =>
        {
            var auth = Credentials(http); await MiniAppProductionRequest.ReadAsync<MiniAppHostActionRequest>(http, 256, ct);
            await service.CloseAsync(auth.Id, auth.Credential, auth.Nonce, auth.Browser, ct);
            http.Response.Cookies.Delete(MiniAppProductionHostApi.RuntimeCookie(auth.Id), new CookieOptions
                { Secure = true, HttpOnly = true, SameSite = SameSiteMode.None, Path = "/" });
            return Results.NoContent();
        });
        group.MapPost("/events", async (HttpContext http, MiniAppProductionRuntimeService service, CancellationToken ct) =>
        {
            var auth = Credentials(http); var request = await MiniAppProductionRequest.ReadAsync<MiniAppProductionEventRequest>(http, 512, ct);
            var code = request.Code switch
            {
                "BRIDGE_READY" => MiniAppProductionEventCode.BridgeReady,
                "BRIDGE_UNAVAILABLE" => MiniAppProductionEventCode.BridgeUnavailable,
                "PERMISSION_DENIED" => MiniAppProductionEventCode.PermissionDenied,
                _ => throw new DeveloperAuthException(400, "INVALID_RUNTIME_EVENT", "Mã sự kiện không hợp lệ.")
            };
            await service.RecordEventAsync(auth.Id, auth.Credential, auth.Nonce, auth.Browser, code, ct); return Results.NoContent();
        });
        group.MapPost("/external-link", async (HttpContext http, MiniAppProductionExternalLinkService service, CancellationToken ct) =>
        {
            var auth = Credentials(http); await MiniAppProductionRequest.ReadAsync<MiniAppHostActionRequest>(http, 256, ct);
            return Results.Ok(await service.PrepareAsync(auth.Id, auth.Credential, auth.Nonce, auth.Browser, ct));
        });
    }
    private static (Guid Id, string? Credential, string Nonce, string? Browser) Credentials(HttpContext http)
    {
        var id = http.Request.Headers["X-Ankt-Host-Session"]; var nonce = http.Request.Headers["X-Ankt-Host-Nonce"];
        if (id.Count != 1 || id.ToString().Length != 36 || !Guid.TryParseExact(id.ToString(), "D", out var session) || nonce.Count != 1 ||
            !MiniAppProductionCredentials.Valid(nonce.ToString()))
            throw new DeveloperAuthException(403, "HOST_AUTH_REQUIRED", "Không thể xác minh phiên Mini App.");
        return (session, MiniAppProductionHostApi.UniqueCookie(http, MiniAppProductionHostApi.RuntimeCookie(session)), nonce.ToString(),
            MiniAppProductionHostApi.UniqueCookie(http, MiniAppProductionHostApi.BindingCookie));
    }
}
