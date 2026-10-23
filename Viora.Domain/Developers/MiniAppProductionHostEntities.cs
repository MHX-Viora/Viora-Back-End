using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public sealed class MiniAppProductionHostSession : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid PublicationId { get; set; }
    public Guid MiniAppVersionId { get; set; }
    public Guid ApprovedReviewRequestId { get; set; }
    public string SnapshotHash { get; set; } = "";
    public Guid PublicationRevision { get; set; }
    public Guid OperationalRevision { get; set; }
    public string PolicyFingerprint { get; set; } = "";
    public Guid? AccountId { get; set; }
    public Guid? SourceSocialSessionId { get; set; }
    public Guid BindingId { get; set; }
    public string BrowserBindingHash { get; set; } = "";
    public string BrowserNonceHash { get; set; } = "";
    public string Platform { get; set; } = "Web";
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public string? RuntimeCredentialHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class MiniAppProductionLaunchGrant : CreatedEntity
{
    public Guid HostSessionId { get; set; }
    public Guid BindingId { get; set; }
    public string GrantHash { get; set; } = "";
    public string BrowserNonceHash { get; set; } = "";
    public string Audience { get; set; } = "";
    // Encrypted, short-lived replay receipt; never a plaintext grant/token column.
    public string? ReceiptProtected { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
