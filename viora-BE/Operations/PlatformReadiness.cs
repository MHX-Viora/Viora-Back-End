using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Persistence;

namespace viora_BE.Operations;

public static class PlatformReadiness
{
    public static void MapPlatformReadiness(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/ready", async (HttpContext http, AppDbContext db) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            bool ready;
            try { ready = await db.Database.CanConnectAsync(timeout.Token); }
            catch (OperationCanceledException) { ready = false; }
            return Results.Json(new { status = ready ? "ready" : "not_ready" },
                statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();
    }
}
