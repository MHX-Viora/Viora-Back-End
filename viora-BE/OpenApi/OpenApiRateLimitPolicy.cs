using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Threading.RateLimiting;

namespace viora_BE.OpenApi;

// Only authenticated claims establish client/user partitions; never trust request parameters.
public sealed class OpenApiRateLimitPolicy : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
    public RateLimitPartition<string> GetPartition(HttpContext context)
    {
        return RateLimitPartition.GetFixedWindowLimiter("ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    }
}

public sealed class OpenApiPrincipalLimiter : IDisposable
{
    public PartitionedRateLimiter<HttpContext> Limiter { get; } = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
            "client:" + context.User.GetClaim("client_id"), _ => new() { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
            "user:" + context.User.GetClaim("client_id") + ":" + context.User.GetClaim("sub"), _ => new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
    public void Dispose() => Limiter.Dispose();
}

public static class OpenApiPrincipalLimits
{
    public static IApplicationBuilder UsePartnerOpenApiPrincipalLimits(this IApplicationBuilder app, PartnerOpenApiOptions settings) => app.Use(async (context, next) =>
    {
        if (settings.Enabled && context.Request.Host.Value.Equals(new Uri(settings.Issuer).Authority, StringComparison.OrdinalIgnoreCase)
            && (context.Request.Path.StartsWithSegments("/v1") || context.Request.Path == "/oidc/userinfo"))
        {
            var result = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (result.Succeeded && result.Principal is not null)
            {
                context.User = result.Principal;
                using var lease = await context.RequestServices.GetRequiredService<OpenApiPrincipalLimiter>().Limiter.AcquireAsync(context, 1, context.RequestAborted);
                if (!lease.IsAcquired) { context.Response.StatusCode = 429; return; }
            }
        }
        await next();
    });
}
