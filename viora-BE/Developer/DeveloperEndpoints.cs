using Microsoft.AspNetCore.Authorization;

namespace viora_BE.Developer;

/// <summary>Public foundation endpoints; no account or Mini App operations are enabled.</summary>
public static class DeveloperEndpoints
{
    public static IEndpointRouteBuilder MapDeveloperEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/developer/health", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new DeveloperHealthResponse("ok", "ankt-developer"));
        })
        .AllowAnonymous()
        .WithName("DeveloperHealth")
        .WithTags("Developer")
        .Produces<DeveloperHealthResponse>();
        return endpoints;
    }
}

public sealed record DeveloperHealthResponse(string Status, string Service);
