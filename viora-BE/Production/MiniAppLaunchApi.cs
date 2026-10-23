using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppLaunchApi
{
    public static void MapMiniAppLaunchApi(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/api/mini-apps").AllowAnonymous().RequireRateLimiting("mini-app-production-launch")
            .AddEndpointFilter<MiniAppProductionFilter>();
        group.MapPost("/{appId}/launch", async (string appId, HttpContext http, MiniAppProductionOptions options,
            MiniAppProductionAccess access, MiniAppProductionLaunchService service, CancellationToken ct) =>
        {
            MiniAppProductionAuthentication.RequireWebOrigin(http, options);
            if (http.Request.QueryString.HasValue) throw new DeveloperAuthException(400, "INVALID_RUNTIME_QUERY", "Tham số truy vấn không hợp lệ.");
            var actor = await MiniAppProductionAuthentication.ResolveAsync(http, access, ct);
            if (appId.Length != 47 || !appId.StartsWith("app_", StringComparison.Ordinal) ||
                !appId.Skip(4).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                throw new DeveloperAuthException(404, "APP_NOT_FOUND", "Mini App hiện không khả dụng.");
            var request = await MiniAppProductionRequest.ReadAsync<MiniAppLaunchRequest>(http, 6144, ct);
            return Results.Ok(await service.LaunchAsync(appId, actor, request, ct));
        });
    }
}
