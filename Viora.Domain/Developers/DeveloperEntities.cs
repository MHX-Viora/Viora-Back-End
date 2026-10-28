using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum DeveloperStatus { PendingVerification, Active, Suspended }
public enum DeveloperTokenPurpose { VerifyEmail, ResetPassword }

public sealed class DeveloperAccount : AuditableEntity
{
    public string FullName { get; set; } = "";
    public string EmailNormalized { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime TermsAcceptedAt { get; set; }
    public string TermsVersion { get; set; } = "developer-2026-10";
    public DeveloperStatus Status { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public Guid SecurityVersion { get; set; } = Guid.NewGuid();
}

public sealed class DeveloperSession : CreatedEntity
{
    public Guid DeveloperAccountId { get; set; }
    public DeveloperAccount Account { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime AbsoluteExpiresAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string DeviceInfo { get; set; } = "";
}

public sealed class DeveloperActionToken : CreatedEntity
{
    public Guid DeveloperAccountId { get; set; }
    public DeveloperAccount Account { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DeveloperTokenPurpose Purpose { get; set; }
    public Guid SecurityVersion { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public sealed class DeveloperExternalIdentity : CreatedEntity
{
    public Guid DeveloperAccountId { get; set; }
    public DeveloperAccount Account { get; set; } = null!;
    public string Provider { get; set; } = "";
    public string ProviderSubjectId { get; set; } = "";
}

public sealed class DeveloperAuditLog : CreatedEntity
{
    public Guid? DeveloperAccountId { get; set; }
    public DeveloperAccount? Account { get; set; }
    public string Action { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public string Metadata { get; set; } = "{}";
}
