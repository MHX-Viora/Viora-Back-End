using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace Viora.Application.Developers;

public sealed record DeveloperRegisterRequest(
    [Required, StringLength(120, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required] string ConfirmPassword, bool AcceptTerms);
public sealed record DeveloperLoginRequest([Required, EmailAddress, StringLength(254)] string Email, [Required, StringLength(128)] string Password, bool RememberMe = false);
public sealed record DeveloperEmailRequest([Required, EmailAddress, StringLength(254)] string Email);
public sealed record DeveloperTokenRequest([Required, StringLength(64, MinimumLength = 43)] string Token);
public sealed record DeveloperResetRequest([Required, StringLength(64, MinimumLength = 43)] string Token, [Required, StringLength(128, MinimumLength = 12)] string Password, [Required] string ConfirmPassword);
public sealed record DeveloperChangePasswordRequest([Required, StringLength(128)] string CurrentPassword, [Required, StringLength(128, MinimumLength = 12)] string Password, [Required] string ConfirmPassword);
public sealed record DeveloperProfileRequest([Required, StringLength(120, MinimumLength = 2)] string FullName, [StringLength(2048)] string? AvatarUrl);
public sealed record DeveloperUnlinkRequest([Required, StringLength(128)] string CurrentPassword);
public sealed record DeveloperAccountDto(Guid Id, string FullName, string Email, DateTime? EmailVerifiedAt, string? AvatarUrl, string Status, bool HasPassword);
public sealed record DeveloperSessionDto(Guid Id, string DeviceInfo, DateTime CreatedAt, DateTime LastUsedAt, DateTime ExpiresAt, bool Current);
public sealed record DeveloperIdentityDto(Guid Id, string Provider, DateTime LinkedAt);
// This secret is sent only to the BFF, never returned by its browser-facing route.
public sealed record DeveloperLoginResult(DeveloperAccountDto Account, string SessionToken, DateTime ExpiresAt, DateTime AbsoluteExpiresAt, bool RememberMe);
public sealed record DeveloperSessionIdentity(Guid SessionId, DeveloperAccountDto Account);
public sealed record DeveloperRequestContext(string IpAddress, string UserAgent);

public sealed class DeveloperAuthException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class DeveloperInput
{
    public static string Email(string? input)
    {
        var email = input?.Trim().Normalize() ?? "";
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !email.Contains('@'))
            throw new DeveloperAuthException(400, "VALIDATION_ERROR", "Email không hợp lệ.");
        return email.ToUpperInvariant();
    }
    public static void Password(string? password, string? confirmation)
    {
        if (password is null || password.Length is < 12 or > 128 || password != confirmation || password.Distinct().Count() < 4)
            throw new DeveloperAuthException(400, "VALIDATION_ERROR", "Mật khẩu cần 12–128 ký tự, đủ đa dạng và xác nhận trùng khớp.");
    }
}

public interface IDeveloperEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken);
}

public interface IDeveloperIdentityService
{
    Task RegisterAsync(DeveloperRegisterRequest request, DeveloperRequestContext context, CancellationToken ct);
    Task VerifyEmailAsync(string token, DeveloperRequestContext context, CancellationToken ct);
    Task SendEmailActionAsync(string email, bool reset, DeveloperRequestContext context, CancellationToken ct);
    Task<DeveloperLoginResult> LoginAsync(DeveloperLoginRequest request, DeveloperRequestContext context, CancellationToken ct);
    Task<DeveloperSessionIdentity?> AuthenticateAsync(string token, CancellationToken ct);
    Task<DeveloperLoginResult> RefreshAsync(Guid sessionId, CancellationToken ct);
    Task LogoutAsync(Guid sessionId, bool all, DeveloperRequestContext context, CancellationToken ct);
    Task ResetPasswordAsync(DeveloperResetRequest request, DeveloperRequestContext context, CancellationToken ct);
    Task ChangePasswordAsync(Guid accountId, DeveloperChangePasswordRequest request, DeveloperRequestContext context, CancellationToken ct);
    Task<DeveloperAccountDto> UpdateProfileAsync(Guid accountId, DeveloperProfileRequest request, DeveloperRequestContext context, CancellationToken ct);
    Task<IReadOnlyList<DeveloperSessionDto>> SessionsAsync(Guid accountId, Guid sessionId, CancellationToken ct);
    Task RevokeSessionAsync(Guid accountId, Guid targetId, DeveloperRequestContext context, CancellationToken ct);
    Task<IReadOnlyList<DeveloperIdentityDto>> LinkedAccountsAsync(Guid accountId, CancellationToken ct);
    Task UnlinkAsync(Guid accountId, Guid identityId, string currentPassword, DeveloperRequestContext context, CancellationToken ct);
}
