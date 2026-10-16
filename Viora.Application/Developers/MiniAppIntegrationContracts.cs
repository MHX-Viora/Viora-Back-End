using System.Net;
using System.Text.RegularExpressions;

namespace Viora.Application.Developers;

public sealed record MiniAppIntegrationRequest(string WebsiteUrl, string SupportEmail, string ShortDescription, string Description,
    string PrivacyPolicyUrl, string TermsOfServiceUrl, bool SsoEnabled, string[] RedirectUris);
public sealed record MiniAppIntegrationDto(string WebsiteUrl, string SupportEmail, string ShortDescription, string Description,
    string PrivacyPolicyUrl, string TermsOfServiceUrl, bool SsoEnabled, string[] RedirectUris);
public sealed record MiniAppDomainDto(string? Hostname, string Status, string? FileName, string? VerificationUrl,
    DateTime? VerifiedAt, DateTime? LastCheckedAt, DateTime? ExpiresAt, string? ErrorCode, string? Message);
public sealed record MiniAppVerificationFile(string FileName, string Content);
public sealed record MiniAppReadinessCheck(string Key, string Requirement, string Status, string Message);
public sealed record MiniAppReadinessDto(bool ConfigurationReady, bool ReadyForSandbox, bool ReadyForReview,
    bool SandboxAvailable, bool ReviewAvailable, DateTime CheckedAt, MiniAppReadinessCheck[] Checks);

public static class PublicWebAddress
{
    public static string Normalize(string? input, bool allowEmpty = false)
    {
        var text = input?.Trim() ?? "";
        if (text == "" && allowEmpty) return "";
        if (text.Length > 2048 || text.Any(char.IsControl) || text.Contains('\\') || text.Contains('#') || text.Contains('*') ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo != "")
            throw MiniAppInput.Invalid("Website và chính sách cần URL HTTPS công khai, cổng 443, không chứa tài khoản hoặc fragment.");
        string host;
        try { host = uri.IdnHost.ToLowerInvariant(); }
        catch (UriFormatException) { throw MiniAppInput.Invalid("Hostname DNS không hợp lệ."); }
        if (host.EndsWith('.')) host = host[..^1];
        if (host.Length > 253 || !host.Contains('.') || IPAddress.TryParse(host, out _) ||
            host.Split('.').Any(label => !Regex.IsMatch(label, "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)) ||
            new[] { "localhost", "local", "internal", "test", "invalid", "example", "onion", "arpa", "home", "lan" }.Any(suffix => host == suffix || host.EndsWith('.' + suffix, StringComparison.Ordinal)))
            throw MiniAppInput.Invalid("Hostname phải là tên miền DNS công khai hợp lệ; không dùng localhost hoặc địa chỉ IP.");
        return new UriBuilder(uri) { Host = host, Port = -1 }.Uri.AbsoluteUri;
    }
    public static string Host(string website) => new Uri(Normalize(website)).IdnHost.ToLowerInvariant();
}

public sealed class PublicFetchException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public sealed record PublicFetchResult(byte[] Content, string ContentType);
public interface IPublicWebFetcher
{
    Task<PublicFetchResult> FetchAsync(Uri uri, int maxBytes, bool verificationFile, CancellationToken ct);
}
