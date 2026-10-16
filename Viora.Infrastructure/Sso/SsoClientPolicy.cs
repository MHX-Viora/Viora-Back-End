using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;

namespace Viora.Infrastructure.Sso;

public static class SsoClientPolicy
{
    public static readonly string[] SupportedScopes = ["openid", "profile", "email"];
    public static string Resource(MiniAppOAuthEnvironment environment) => "ankt-miniapp-sso:" + environment;

    public static void EnsureEligible(MiniAppOAuthClient client, MiniApp app, MiniAppDomainVerification? proof,
        IEnumerable<string> scopes, string environment, DateTime now)
    {
        if (client.Environment.ToString() != environment || client.RevokedAt != null || app.DeletedAt != null ||
            !app.SsoEnabled || app.Developer.Status != DeveloperStatus.Active ||
            app.Status is MiniAppStatus.Suspended or MiniAppStatus.Rejected ||
            (client.Environment == MiniAppOAuthEnvironment.Production ? app.Status != MiniAppStatus.Published : !client.SandboxEnabled))
            throw new DeveloperAuthException(403, "SSO_CLIENT_UNAVAILABLE", "Ứng dụng chưa được phép đăng nhập ANKT trong môi trường này.");
        if (proof is not { Status: MiniAppDomainStatus.Verified, RevokedAt: null } || proof.MiniAppId != app.Id ||
            proof.ExpiresAt <= now || proof.Hostname != MiniAppDomainLifecycle.Host(app.WebsiteUrl))
            throw new DeveloperAuthException(403, "SSO_DOMAIN_UNVERIFIED", "Domain ứng dụng chưa được xác minh hoặc đã hết hạn.");
        var requested = scopes.ToArray();
        var allowed = client.AllowedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!requested.Contains("openid", StringComparer.Ordinal) || requested.Any(x => !SupportedScopes.Contains(x, StringComparer.Ordinal) || !allowed.Contains(x, StringComparer.Ordinal)))
            throw new DeveloperAuthException(400, "SSO_INVALID_SCOPE", "Quyền truy cập không hợp lệ.");
    }

    public static bool IsRedirectAllowed(MiniAppOAuthClient client, IEnumerable<MiniAppRedirectUri> redirects, string uri) =>
        redirects.Any(x => x.MiniAppId == client.MiniAppId && x.Environment == client.Environment &&
            string.Equals(x.RedirectUri, uri, StringComparison.Ordinal));
}
