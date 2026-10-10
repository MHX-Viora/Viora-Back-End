using System.Globalization;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Viora.Application.MiniApps;

public static class MiniAppSecurityPolicy
{
    // A catalogue label must not advertise a capability the host does not implement.
    // New scopes require an implemented host/API path and its consent semantics.
    public static readonly string[] SupportedPermissionCodes = ["identity.login", "profile.basic", "profile.email", "profile.phone", "app.close", "app.open_url", "app.theme"];
    public static bool IsAllowedHttpsUrl(string? value, IEnumerable<string> allowedDomains)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443 || !IsPublicHost(uri.IdnHost))
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

    public static bool IsPublicHost(string host) =>
        Uri.CheckHostName(host) == UriHostNameType.Dns && host.Contains('.') &&
        !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
        !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
                !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) &&
                !(b[0] == 192 && b[1] == 88 && b[2] == 99) &&
                !(b[0] == 198 && (b[1] == 18 || b[1] == 19 || b[1] == 51)) &&
                !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Accept global IPv6 unicast only; exclude documentation and IPv4 tunnel ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] == 0x0d && b[3] == 0xb8 || b[2] < 2)) &&
            !(b[0] == 0x20 && b[1] == 0x02) && !(b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0);
    }

    public static bool IsExactRedirect(string redirect, IEnumerable<string> callbacks) =>
        callbacks.Contains(redirect, StringComparer.Ordinal) && Uri.TryCreate(redirect, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.Fragment);

    public static string CreatePkceChallenge(string verifier) => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool ValidatePkce(string? verifier, string? challenge) =>
        verifier is { Length: >= 43 and <= 128 } && verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~') &&
        challenge is { Length: 43 } && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CreatePkceChallenge(verifier)), Encoding.ASCII.GetBytes(challenge));

    public static string BuildLaunchUrl(string callbackUrl, string code, string? state = null)
    {
        var builder = new UriBuilder(callbackUrl);
        var values = ParseQuery(builder.Query);
        values["code"] = code;
        if (state is not null) values["state"] = state;
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
