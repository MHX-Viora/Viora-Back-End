using System.ComponentModel.DataAnnotations;

namespace Viora.Application.Developers;

public sealed record MiniAppCreateRequest(string Name, string Category, Guid RequestId, Guid? LogoUploadId = null);
public sealed record MiniAppUpdateRequest(string? Name = null, string? Category = null, string? ShortDescription = null,
    string? Description = null, string? WebsiteUrl = null, string? SupportEmail = null, string? PrivacyPolicyUrl = null,
    string? TermsOfServiceUrl = null, Guid? LogoUploadId = null);
public sealed record MiniAppDto(string AppId, string Name, string Category, string? LogoUrl, string? ShortDescription,
    string? Description, string? WebsiteUrl, string? SupportEmail, string? PrivacyPolicyUrl, string? TermsOfServiceUrl,
    string Status, DateTime CreatedAt, DateTime UpdatedAt, Guid AppVersion);
public sealed record MiniAppActivityDto(string Action, DateTime CreatedAt);
public sealed record MiniAppLogoDto(Guid UploadId, string Url);

public static class MiniAppInput
{
    public static readonly string[] Categories = ["utilities", "shopping", "education", "services", "entertainment", "other"];
    public static DeveloperAuthException Invalid(string message) => new(400, "VALIDATION_ERROR", message);
    public static string Text(string? input, int max, bool required = false)
    {
        var text = input?.Trim() ?? "";
        if (text.Length > max || (required && text.Length < 2) || text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
            throw Invalid("Thông tin vượt giới hạn hoặc chưa đầy đủ.");
        return text;
    }
    public static void ValidateCreate(MiniAppCreateRequest r)
    {
        Text(r.Name, 120, true);
        if (!Categories.Contains(r.Category) || r.RequestId == Guid.Empty) throw Invalid("Danh mục hoặc mã yêu cầu không hợp lệ.");
    }
    public static string Url(string? input, bool development, bool redirect = false)
    {
        var text = Text(input, redirect ? 1024 : 2048);
        if (text == "" && !redirect) return text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || uri.UserInfo != "" ||
            text.Contains('*') || (redirect && text.Contains('#')) ||
            (uri.Scheme != "https" && !(development && uri.Scheme == "http" &&
                (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1" || uri.Host == "[::1]"))))
            throw Invalid("URL phải dùng HTTPS, không chứa tài khoản; callback không được có wildcard hoặc fragment. HTTP localhost chỉ dùng khi phát triển.");
        return text;
    }
}
