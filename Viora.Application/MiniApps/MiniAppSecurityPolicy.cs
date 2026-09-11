using System.Globalization;
using System.Text;

namespace Viora.Application.MiniApps;

public static class MiniAppSecurityPolicy
{
    public static bool IsAllowedHttpsUrl(string? value, IEnumerable<string> allowedDomains)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        try
        {
            var host = NormalizeHost(uri.IdnHost);
            return allowedDomains.Any(domain => MatchesDomain(host, domain));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string BuildLaunchUrl(string callbackUrl, string code)
    {
        var builder = new UriBuilder(callbackUrl);
        var values = ParseQuery(builder.Query);
        values["code"] = code;
        builder.Query = string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri.AbsoluteUri;
    }

    public static MiniAppProjectedProfile ProjectProfile(
        string subject, string? displayName, string? avatarUrl, string? email, string? phone,
        IReadOnlyCollection<string> grantedScopes)
    {
        var basic = grantedScopes.Contains("profile.basic", StringComparer.Ordinal);
        return new(
            grantedScopes.Contains("identity.login", StringComparer.Ordinal) ? subject : null,
            basic ? displayName : null,
            basic ? avatarUrl : null,
            grantedScopes.Contains("profile.email", StringComparer.Ordinal) ? email : null,
            grantedScopes.Contains("profile.phone", StringComparer.Ordinal) ? phone : null,
            grantedScopes.Order(StringComparer.Ordinal).ToArray());
    }

    public static string[] NormalizeDomains(IEnumerable<string> values)
    {
        var domains = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var candidate = value.Trim().TrimEnd('.').ToLowerInvariant();
            if (candidate.Length == 0 || candidate.Contains('/') || candidate.Contains(':')) continue;
            try { domains.Add(NormalizeHost(candidate)); }
            catch (ArgumentException) { }
        }
        return domains.ToArray();
    }

    private static bool MatchesDomain(string host, string pattern)
    {
        var normalized = NormalizeHost(pattern.Trim());
        if (normalized.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = normalized[2..];
            return host.Length > suffix.Length && host.EndsWith('.' + suffix, StringComparison.Ordinal);
        }
        return host.Equals(normalized, StringComparison.Ordinal);
    }

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            return "*." + new IdnMapping().GetAscii(trimmed[2..]);
        }
        return new IdnMapping().GetAscii(trimmed);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? item : item[..separator]);
            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(item[(separator + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
