using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Viora.Infrastructure.Developers;
using viora_BE.Admin;

namespace viora_BE.Developer;

public static class MiniAppReviewRegistration
{
    public static IServiceCollection AddMiniAppReviewServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddScoped<MiniAppReviewAccess>();
        services.TryAddScoped<MiniAppVersionService>();
        services.TryAddScoped<MiniAppVersionCredentials>();
        services.TryAddScoped<MiniAppReviewAuditService>();
        services.TryAddScoped<MiniAppReadinessService>();
        services.TryAddScoped<MiniAppPublishingService>();
        services.TryAddScoped<MiniAppCatalogProjection>();
        services.TryAddScoped<MiniAppModerationService>();
        services.TryAddScoped<MiniAppReviewSubmissionService>();
        services.TryAddScoped<MiniAppReviewService>();
        services.TryAddScoped<MiniAppReviewDecisionService>();
        services.TryAddScoped<MiniAppReviewNotificationService>();
        services.TryAddScoped<MiniAppReviewEmailDelivery>();
        services.AddHostedService<MiniAppReviewNotificationWorker>();
        services.AddOptions<MiniAppReviewOptions>().Configure(o => configuration?.GetSection(MiniAppReviewOptions.SectionName).Bind(o))
            .Validate(o => o.ReadLimitPerMinute is >= 1 and <= 1000 && o.MutationLimitPerMinute is >= 1 and <= 120 &&
                o.CredentialLimitPerMinute is >= 1 and <= 30, "Mini App review limits must be bounded.").ValidateOnStart();
        foreach (var policy in new[] { "mini-app-review-read", "mini-app-review-write", "mini-app-review-credentials" })
            services.AddRateLimiter(options => options.AddPolicy(policy, http =>
            {
                var limits = http.RequestServices.GetRequiredService<IOptions<MiniAppReviewOptions>>().Value;
                var limit = policy == "mini-app-review-read" ? limits.ReadLimitPerMinute : policy == "mini-app-review-write" ? limits.MutationLimitPerMinute : limits.CredentialLimitPerMinute;
                var partition = http.User.FindFirstValue("developer_id") ?? (http.User.FindFirstValue("sub") is { } admin ? "admin:" + admin : "anonymous");
                return RateLimitPartition.GetFixedWindowLimiter(partition,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            }));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, MiniAppReviewAuthorization>());
        services.AddAuthorization(MiniAppReviewAuthorization.Configure);
        return services;
    }
}
