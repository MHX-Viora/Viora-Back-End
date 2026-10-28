using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;

namespace viora_BE.Admin;

public static class MiniAppModerationApi
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value.Enabled) return;
        var group = endpoints.MapGroup("/api/admin/mini-apps").RequireAuthorization(MiniAppReviewAuthorization.Suspend)
            .RequireRateLimiting("mini-app-review-write").AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer"; return await next(context);
            });
        foreach (var route in new[] { "suspend", "restore" })
            group.MapPost("/{appId}/" + route, async (string appId, HttpContext http, MiniAppModerationService service, CancellationToken ct) =>
            {
                MiniAppReviewAdminApi.Query(http);
                var request = await MiniAppVersionApi.Read<MiniAppModerationRequest>(http, 4096, ct);
                var admin = MiniAppReviewAdminApi.Admin(http); var source = MiniAppReviewAdminApi.Source(http);
                return Results.Ok(route == "suspend" ? await service.SuspendAsync(admin, source, appId, request, ct) : await service.RestoreAsync(admin, source, appId, request, ct));
            });
    }
}
