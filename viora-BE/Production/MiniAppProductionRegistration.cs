using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Viora.Infrastructure.Developers;

namespace viora_BE.Production;

public static class MiniAppProductionRegistration
{
    public static IServiceCollection AddMiniAppProductionWorker(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("MiniAppRuntime:Enabled") || configuration.GetValue<bool>("MiniAppRuntime:CleanupEnabled"))
            services.AddHostedService<MiniAppProductionWorker>();
        return services;
    }
    public static IServiceCollection AddMiniAppProductionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddOptions<MiniAppProductionOptions>().Configure(o =>
        {
            configuration.GetSection(MiniAppProductionOptions.SectionName).Bind(o);
            o.ReserveConfiguredHost(configuration["MiniAppAuth:AllowedHostOrigin"]);
            o.ReserveConfiguredHost(configuration["MiniAppSandbox:HostOrigin"]);
        }).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MiniAppProductionOptions>>(sp => new MiniAppProductionOptionsValidator(
            sp.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value));
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<MiniAppProductionOptions>>().Value);
        services.TryAddScoped(sp => new MiniAppPublicCatalogService(sp.GetRequiredService<Viora.Infrastructure.Persistence.AppDbContext>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<MiniAppProductionOptions>(), configuration.GetValue<bool>("MiniAppAuth:Enabled")));
        services.TryAddScoped<MiniAppCatalogProjection>();
        services.AddDataProtection();
        services.TryAddScoped<MiniAppProductionBindingService>();
        services.TryAddScoped<MiniAppProductionAccess>();
        services.TryAddScoped<MiniAppProductionAudit>();
        services.TryAddScoped(sp => new MiniAppProductionPolicy(sp.GetRequiredService<Viora.Infrastructure.Persistence.AppDbContext>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<MiniAppProductionOptions>()));
        services.TryAddScoped<MiniAppProductionLaunchService>();
        services.TryAddScoped<MiniAppProductionGrantService>();
        services.TryAddScoped<MiniAppProductionRuntimeService>();
        services.TryAddScoped<MiniAppProductionRetentionService>();
        services.TryAddScoped<MiniAppProductionExternalLinkService>();
        services.AddRateLimiter(o => o.AddPolicy("mini-app-public-read", new PublicReadPolicy(options => options.ReadLimitPerMinute)));
        services.AddRateLimiter(o => o.AddPolicy("mini-app-host-prepare", new PublicReadPolicy(options => options.PrepareLimitPerMinute)));
        services.AddRateLimiter(o => o.AddPolicy("mini-app-production-launch", new PublicReadPolicy(options => options.LaunchLimitPerMinute)));
        services.AddRateLimiter(o => o.AddPolicy("mini-app-host-redeem", new PublicReadPolicy(options => options.RedeemLimitPerMinute)));
        services.AddRateLimiter(o => o.AddPolicy("mini-app-host-runtime", new PublicReadPolicy(options => options.RuntimeLimitPerMinute)));
        return services;
    }

    private sealed class PublicReadPolicy(Func<MiniAppProductionOptions, int> limit) : IRateLimiterPolicy<string>
    {
        public RateLimitPartition<string> GetPartition(HttpContext http) => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
            {
                PermitLimit = limit(http.RequestServices.GetRequiredService<MiniAppProductionOptions>()),
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            });

        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            await context.HttpContext.Response.WriteAsJsonAsync(new { error = new { code = "RATE_LIMITED", message = "Quá nhiều yêu cầu. Vui lòng thử lại sau." } }, ct);
        };
    }
}
