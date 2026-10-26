using System.Text.Json;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class SandboxTestApi
{
    public const string CookieName = "__Host-AnktSandboxTest";
    public static string ClientCookie(string clientId) => CookieName + "-" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clientId)))[..16];
    public static void MapEndpoints(RouteGroupBuilder apps)
    {
        var group = apps.MapGroup("/{appId}/sandbox-test");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
            return await next(context);
        });
        group.MapGet("", async (string appId, HttpContext http, SandboxTestService tests, CancellationToken ct) =>
        {
            var actor = MiniAppSandboxApi.Actor(http, false);
            return Results.Ok(await tests.OverviewAsync(actor.Id, actor.SourceSessionId, appId, ct));
        });
        group.MapPost("/sessions", async (string appId, HttpContext http, SandboxTestService tests, CancellationToken ct) =>
        {
            var body = await MiniAppSandboxApi.ReadRequest<JsonElement>(http, ct);
            if (body.EnumerateObject().Any()) throw Viora.Application.Developers.MiniAppInput.Invalid("Không truyền danh tính hoặc môi trường khi tạo phiên test.");
            var actor = MiniAppSandboxApi.Actor(http, false);
            return Results.Ok(await tests.CreateAsync(actor.Id, actor.SourceSessionId, appId, ct));
        }).RequireRateLimiting("mini-app-sensitive");
        group.MapDelete("/sessions/{sessionId:guid}", async (string appId, Guid sessionId, HttpContext http, SandboxTestService tests, CancellationToken ct) =>
        {
            var actor = MiniAppSandboxApi.Actor(http, false);
            await tests.RevokeAsync(actor.Id, actor.SourceSessionId, appId, sessionId, ct);
            return Results.NoContent();
        }).RequireRateLimiting("mini-app-sensitive");
    }
}
