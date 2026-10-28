namespace Viora.Application.Developers;

public sealed record SandboxTestProfile(string FullName, string DisplayName, string Nickname, string Email,
    string Gender, string Birthdate, string? AvatarUrl, string? CoverUrl, bool EmailVerified, string AccountType,
    Guid Id = default, string Username = "", bool IsSandbox = true, bool IsVerified = false, bool IsActive = true);
public sealed record SandboxTestSessionDto(Guid Id, DateTime ExpiresAt, DateTime? RevokedAt,
    DateTime? AuthorizedAt, DateTime? TokenIssuedAt, DateTime? UserInfoAt);
public sealed record SandboxTestOverview(SandboxTestProfile Profile, bool Ready, string? ErrorCode,
    string? ClientId, string[] RedirectUris, string? LaunchEndpoint, SandboxTestSessionDto[] Sessions);
public sealed record SandboxTestLaunch(Guid SessionId, string LaunchEndpoint, string LaunchTicket, DateTime ExpiresAt);
