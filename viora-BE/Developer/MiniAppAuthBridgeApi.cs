using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppAuthBridgeApi
{
    public static IServiceCollection AddMiniAppAuthBridge(this IServiceCollection services, IConfiguration config)
    {
        SandboxTestRegistration.AddSandboxTestIdentity(services, config);
        services.AddOptions<MiniAppBridgeOptions>().Bind(config.GetSection("MiniAppAuth"))
            .Validate(x => !x.Enabled || SandboxTestOptions.ValidPortalOrigin(x.AllowedHostOrigin) && x.AllowedHostOrigin.StartsWith("https://", StringComparison.Ordinal),
                "MiniAppAuth requires an exact HTTPS AllowedHostOrigin.");
        services.AddScoped<MiniAppBridgeService>();
        // No credential cookies are accepted by these endpoints. Actual origin
        // authorization is checked against the launch/session after CORS preflight.
        services.AddCors(o => o.AddPolicy("MiniAppAuthBridge", p => p.SetIsOriginAllowed(origin =>
            SandboxTestOptions.ValidPortalOrigin(origin) && origin.StartsWith("https://", StringComparison.Ordinal))
            .WithMethods("GET", "POST").WithHeaders("Content-Type", "Authorization", "X-Ankt-Platform", "X-Ankt-MiniApp-Client")));
        services.AddRateLimiter(o => o.AddPolicy("miniapp-auth-bridge", h => RateLimitPartition.GetFixedWindowLimiter(
            h.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }

    private static string Origin(HttpContext http)
    {
        var value = http.Request.Headers.Origin;
        if (value.Count != 1 || !SandboxTestOptions.ValidPortalOrigin(value.ToString()) || !value.ToString().StartsWith("https://", StringComparison.Ordinal))
            throw new DeveloperAuthException(403, "INVALID_ORIGIN", "Nguồn Mini App không hợp lệ.");
        return value.ToString();
    }
    private static string Bearer(HttpContext http)
    {
        var value = http.Request.Headers.Authorization;
        if (value.Count != 1 || !value.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
            throw new DeveloperAuthException(401, "SESSION_EXPIRED", "Phiên Mini App không hợp lệ.");
        return value.ToString()[7..];
    }
    private static string Client(HttpContext http)
    {
        var value = http.Request.Headers["X-Ankt-MiniApp-Client"];
        if (value.Count != 1 || !System.Text.RegularExpressions.Regex.IsMatch(value.ToString(), "^app_[A-Za-z0-9_-]{43}$"))
            throw new DeveloperAuthException(400, "INVALID_CLIENT", "Cần Client ID của Mini App.");
        return value.ToString();
    }
    private static async Task<MiniAppBridgeActor> Actor(HttpContext http)
    {
        var developer = http.Request.Headers.Authorization.ToString().StartsWith("DeveloperSession ", StringComparison.Ordinal);
        var result = await http.AuthenticateAsync(developer ? DeveloperSessionHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded || (!developer && result.Principal?.FindFirst("token_type")?.Value != "access") ||
            !Guid.TryParse(result.Principal?.FindFirst(developer ? "developer_id" : "sub")?.Value, out var id) ||
            !Guid.TryParse(result.Principal?.FindFirst(developer ? "developer_session" : "sid")?.Value, out var source))
            throw new DeveloperAuthException(401, "SESSION_EXPIRED", "Vui lòng đăng nhập ANKT.");
        return new(id, source, developer);
    }
    private static void HostOrigin(HttpContext http, MiniAppBridgeOptions options)
    {
        if (http.Request.Headers.Origin.Count == 0 && http.Request.Headers["X-Ankt-Platform"].Count == 1 &&
            http.Request.Headers["X-Ankt-Platform"].ToString() is "Android" or "iOS") return;
        if (Origin(http) != options.AllowedHostOrigin) throw new DeveloperAuthException(403, "INVALID_ORIGIN", "Chỉ ANKT Host được yêu cầu đăng nhập.");
    }
    public static IEndpointRouteBuilder MapMiniAppAuthBridge(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/miniapp/auth").AllowAnonymous().RequireCors("MiniAppAuthBridge")
            .RequireRateLimiting("miniapp-auth-bridge").AddEndpointFilter<DeveloperApiFilter>();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!http.Request.IsHttps || http.Request.QueryString.HasValue && !http.Request.Path.Value!.EndsWith("/config", StringComparison.Ordinal))
                throw new DeveloperAuthException(400, "INVALID_REQUEST", "Yêu cầu Mini App không hợp lệ.");
            return await next(context);
        });
        group.MapGet("/config", async (string clientId, MiniAppBridgeService service, CancellationToken ct) => Results.Ok(await service.ConfigAsync(clientId, null, ct)));
        group.MapPost("/launch", async (HttpContext http, IOptions<MiniAppBridgeOptions> options, MiniAppBridgeService service, CancellationToken ct) =>
        {
            HostOrigin(http, options.Value); var actor = await Actor(http);
            if (actor.IsSandbox) throw new DeveloperAuthException(403, "SANDBOX_NOT_ALLOWED", "Dùng Sandbox trong Developer Portal.");
            return Results.Ok(await service.LaunchAsync(actor, await MiniAppSandboxApi.ReadRequest<MiniAppBridgeLaunchRequest>(http, ct), ct));
        });
        group.MapPost("/exchange", async (HttpContext http, MiniAppBridgeService service, CancellationToken ct) =>
            Results.Ok(await service.ExchangeAsync(await MiniAppSandboxApi.ReadRequest<MiniAppBridgeExchange>(http, ct), Origin(http), ct)));
        group.MapGet("/me", async (HttpContext http, MiniAppBridgeService service, CancellationToken ct) => Results.Ok(await service.MeAsync(Bearer(http), Origin(http), ct, Client(http))));
        group.MapPost("/revoke", async (HttpContext http, IOptions<MiniAppBridgeOptions> options, MiniAppBridgeService service, CancellationToken ct) =>
        {
            var token = Bearer(http);
            if (token.StartsWith("mas_", StringComparison.Ordinal) || token.StartsWith("map_", StringComparison.Ordinal))
            {
                await MiniAppSandboxApi.ReadRequest<System.Text.Json.JsonElement>(http, ct);
                var session = await service.AuthenticateAsync(token, Origin(http), ct, Client(http)); await service.RevokeTokenAsync(session.Id, ct);
            }
            else
            {
                HostOrigin(http, options.Value); var actor = await Actor(http);
                var request = await MiniAppSandboxApi.ReadRequest<RevokeRequest>(http, ct); await service.RevokeAsync(request.SessionId, actor, ct);
            }
            return Results.NoContent();
        });
        return endpoints;
    }
    public sealed record RevokeRequest(Guid SessionId);
    public static void MapOwnerEndpoints(RouteGroupBuilder apps)
    {
        var group = apps.MapGroup("/{appId}/auth-bridge");
        group.MapGet("", async (string appId, HttpContext http, MiniAppBridgeService service, CancellationToken ct) =>
        { var actor = await Actor(http); return Results.Ok(await service.ConfigAsync(appId, actor, ct)); });
        group.MapPost("/launch", async (string appId, HttpContext http, MiniAppBridgeService service, CancellationToken ct) =>
        {
            var actor = await Actor(http); var request = await MiniAppSandboxApi.ReadRequest<MiniAppBridgeLaunchRequest>(http, ct);
            if (request.ClientId != appId) throw new DeveloperAuthException(400, "INVALID_CLIENT", "Ứng dụng không hợp lệ.");
            return Results.Ok(await service.LaunchAsync(actor, request, ct));
        }).RequireRateLimiting("mini-app-sensitive");
        group.MapPost("/revoke", async (string appId, HttpContext http, MiniAppBridgeService service, CancellationToken ct) =>
        {
            var actor = await Actor(http); await service.ConfigAsync(appId, actor, ct);
            var request = await MiniAppSandboxApi.ReadRequest<RevokeRequest>(http, ct); await service.RevokeAsync(request.SessionId, actor, ct, appId);
            return Results.NoContent();
        });
    }
}
