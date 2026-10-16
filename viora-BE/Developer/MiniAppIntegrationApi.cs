using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;
public static class MiniAppIntegrationApi
{
    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<IPublicDnsResolver, PublicDnsResolver>();
        services.AddSingleton<IPublicSocketConnector, PublicSocketConnector>();
        services.AddSingleton<IPublicWebFetcher, PublicWebFetcher>();
        services.AddScoped<MiniAppIntegrationService>();
        services.AddRateLimiter(o => o.AddPolicy("mini-app-verification", h => RateLimitPartition.GetFixedWindowLimiter(
            h.User.FindFirstValue("developer_id") ?? "anonymous", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
    }
    public static void MapEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/{appId}/integration", async (string appId, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Get(Owner(h), appId, ct)));
        g.MapPut("/{appId}/integration", async (string appId, MiniAppIntegrationRequest r, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Save(Owner(h), appId, r, ct)));
        g.MapGet("/{appId}/domain-verification", async (string appId, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Domain(Owner(h), appId, ct)));
        g.MapGet("/{appId}/domain-verification/file", async (string appId, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => {
            var file = await s.File(Owner(h), appId, ct);
            return Results.File(Encoding.UTF8.GetBytes(file.Content), "text/plain; charset=utf-8", file.FileName);
        }).RequireRateLimiting("mini-app-verification");
        g.MapPost("/{appId}/domain-verification/verify", async (string appId, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Verify(Owner(h), appId, ct))).RequireRateLimiting("mini-app-verification");
        g.MapPost("/{appId}/domain-verification/regenerate", async (string appId, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Regenerate(Owner(h), appId, ct))).RequireRateLimiting("mini-app-verification");
        g.MapGet("/{appId}/readiness", async (string appId, MiniAppIntegrationService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.Readiness(Owner(h), appId, ct))).RequireRateLimiting("mini-app-verification");
    }
    private static Guid Owner(HttpContext h) => Guid.Parse(h.User.FindFirstValue("developer_id")!);
}
