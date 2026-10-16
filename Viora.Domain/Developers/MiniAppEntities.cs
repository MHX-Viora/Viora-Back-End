using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum MiniAppStatus { Draft, ConfigurationIncomplete, PendingReview, ChangesRequested, Rejected, Approved, Published, Suspended }

public sealed class MiniApp : AuditableEntity
{
    public string AppId { get; set; } = "";
    public Guid DeveloperAccountId { get; set; }
    public DeveloperAccount Developer { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? SupportEmail { get; set; }
    public MiniAppStatus Status { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid CreateRequestId { get; set; }
    public string CreateFingerprint { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class MiniAppOAuthClient : AuditableEntity
{
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public string ClientKey { get; set; } = "";
    public string ClientType { get; set; } = "Confidential";
    public DateTime? RevokedAt { get; set; }
}

public sealed class MiniAppClientSecret : CreatedEntity
{
    public Guid OAuthClientId { get; set; }
    public MiniAppOAuthClient OAuthClient { get; set; } = null!;
    public string SecretEncrypted { get; set; } = "";
    public string SecretKeyVersion { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class MiniAppRedirectUri : AuditableEntity
{
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public string RedirectUri { get; set; } = "";
}

public sealed class MiniAppAuditLog : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public Guid ActorDeveloperId { get; set; }
    public DeveloperAccount Actor { get; set; } = null!;
    public string Action { get; set; } = "";
    public string Metadata { get; set; } = "{}";
}

public sealed class MiniAppLogoUpload : CreatedEntity
{
    public Guid DeveloperAccountId { get; set; }
    public DeveloperAccount Developer { get; set; } = null!;
    public string PublicId { get; set; } = "";
    public string? Url { get; set; }
    public Guid? MiniAppId { get; set; }
    public MiniApp? MiniApp { get; set; }
    public DateTime ExpiresAt { get; set; }
}
