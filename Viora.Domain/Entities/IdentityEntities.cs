namespace Viora.Domain.Entities;

public sealed class Account : AuditableEntity
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = null!;
    public AccountRole Role { get; set; } = AccountRole.User;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public User? User { get; set; }
}

public sealed class User : AuditableEntity
{
    public Guid AccountId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public Gender Gender { get; set; } = Gender.Unknown;
    public bool IsVerified { get; set; }
    public UserIdentityState IdentityStatus { get; set; } = UserIdentityState.NotVerified;
    public Account Account { get; set; } = null!;
    public UserSettings? Settings { get; set; }
    public ICollection<UserIdentity> IdentitySubmissions { get; set; } = [];
}

public sealed class UserIdentity : AuditableEntity
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string IdentityNumber { get; set; } = null!;
    public DateOnly? Birthday { get; set; }
    public string FrontImageUrl { get; set; } = null!;
    public string BackImageUrl { get; set; } = null!;
    public IdentitySubmissionStatus Status { get; set; } = IdentitySubmissionStatus.Pending;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
    public User User { get; set; } = null!;
    public User? Reviewer { get; set; }
}

public sealed class UserSettings : AuditableEntity
{
    public Guid UserId { get; set; }
    public bool IsPrivate { get; set; }
    public bool AllowMessageEveryone { get; set; } = true;
    public bool AllowComment { get; set; } = true;
    public bool AllowMention { get; set; } = true;
    public string Language { get; set; } = "vi";
    public string Theme { get; set; } = "light";
    public User User { get; set; } = null!;
}
