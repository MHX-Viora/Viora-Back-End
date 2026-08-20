namespace Viora.Domain.Entities;

public sealed class Developer : AuditableEntity
{
    public Guid? AccountId { get; set; }
    public string Name { get; set; } = null!;
    public string? CompanyName { get; set; }
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public DeveloperStatus Status { get; set; } = DeveloperStatus.Pending;
    public Account? Account { get; set; }
    public ICollection<MiniApp> MiniApps { get; set; } = [];
}

public sealed class MiniApp : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string WebUrl { get; set; } = null!;
    public string CallbackUrl { get; set; } = null!;
    public Guid DeveloperId { get; set; }
    public string ClientId { get; set; } = null!;
    public string ClientSecretHash { get; set; } = null!;
    public DateTime? SecretRotatedAt { get; set; }
    public MiniAppStatus Status { get; set; } = MiniAppStatus.Draft;
    public bool IsFeatured { get; set; }
    public string[] AllowedDomains { get; set; } = [];
    public DateTime? DeletedAt { get; set; }
    public Developer Developer { get; set; } = null!;
    public ICollection<MiniAppPermissionMapping> PermissionMappings { get; set; } = [];
}

public sealed class MiniAppPermission : AuditableEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
    public MiniAppPermissionStatus Status { get; set; } = MiniAppPermissionStatus.Active;
    public ICollection<MiniAppPermissionMapping> MiniAppMappings { get; set; } = [];
}

public sealed class MiniAppPermissionMapping
{
    public Guid MiniAppId { get; set; }
    public Guid PermissionId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public MiniAppPermission Permission { get; set; } = null!;
}

public sealed class MiniAppUserConsent : AuditableEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public Guid PermissionId { get; set; }
    public bool Granted { get; set; }
    public DateTime? GrantedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Account Account { get; set; } = null!;
    public MiniApp MiniApp { get; set; } = null!;
    public MiniAppPermission Permission { get; set; } = null!;
}

public sealed class MiniAppLaunchCode : CreatedEntity
{
    public string CodeHash { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Account Account { get; set; } = null!;
    public MiniApp MiniApp { get; set; } = null!;
}

public sealed class MiniAppExternalIdentity : AuditableEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public string Subject { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public MiniApp MiniApp { get; set; } = null!;
}

public sealed class MiniAppAuditLog : CreatedEntity
{
    public Guid? MiniAppId { get; set; }
    public Guid? DeveloperId { get; set; }
    public Guid? ActorAccountId { get; set; }
    public string Action { get; set; } = null!;
    public string? Detail { get; set; }
}

public sealed class MiniAppLaunchLog : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid? AccountId { get; set; }
    public MiniAppLaunchStatus Status { get; set; }
    public string? FailureReason { get; set; }
}
