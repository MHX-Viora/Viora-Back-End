using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Viora.Infrastructure.Developers;

namespace viora_BE.Admin;

public sealed record MiniAppReviewRequirement : IAuthorizationRequirement;

public sealed class MiniAppReviewAuthorization(MiniAppReviewAccess access) : AuthorizationHandler<MiniAppReviewRequirement>
{
    public const string Read = "MiniAppReview.Read";
    public const string Decide = "MiniAppReview.Decide";
    public const string Suspend = "MiniAppReview.Suspend";

    public static void Configure(AuthorizationOptions options)
    {
        foreach (var policy in new[] { Read, Decide, Suspend })
            options.AddPolicy(policy, builder => builder.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireRole("2").RequireClaim("token_type", "access")
                .AddRequirements(new MiniAppReviewRequirement()));
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, MiniAppReviewRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirstValue("sub"), out var account) ||
            !Guid.TryParse(context.User.FindFirstValue("sid"), out var session)) return;
        var ct = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        if (await access.IsLiveAdminAsync(account, session, ct)) context.Succeed(requirement);
    }
}
