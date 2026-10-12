using Viora.Application.MiniApps;

namespace Viora.Infrastructure.OpenApi;

public static class OpenApiPolicy
{
    public static readonly IReadOnlyDictionary<string, string> ScopePermissions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["openid"] = "identity.login", ["profile"] = "profile.basic", ["email"] = "profile.email", ["phone"] = "profile.phone"
    };
    public static string[] PermissionsFor(IEnumerable<string> scopes) => scopes.Select(s => ScopePermissions.TryGetValue(s, out var code) ? code : throw new ArgumentException("Unsupported scope")).Distinct().ToArray();
    public static bool IsPublicPath(string path)
    {
        if (new[] { "/health", "/.well-known/openid-configuration", "/oauth/jwks", "/oauth/authorize", "/oauth/token", "/oauth/revoke", "/oidc/userinfo", "/v1/me", "/v1/me/profile", "/v1/account-links", "/mini-app-sdk/ankt-mini-app.js" }.Contains(path, StringComparer.Ordinal)) return true;
        var parts = path.Split('/');
        return parts.Length == 5 && parts[1] == "v1" && parts[2] == "apps" && Guid.TryParse(parts[3], out _) && parts[4] == "permissions" ||
            parts.Length == 4 && parts[1] == "v1" && parts[2] == "account-links" && Guid.TryParse(parts[3], out _);
    }
    public static bool ValidTransaction(string redirect, IEnumerable<string> callbacks, string state, string challenge, string method, string nonce) =>
        MiniAppSecurityPolicy.IsExactRedirect(redirect, callbacks) && Uri.TryCreate(redirect, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        state.Length is >= 16 and <= 512 && nonce.Length is >= 16 and <= 256 && method == "S256" && challenge.Length == 43 &&
        challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
