using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

// Immutable query projection. Live publication and safety records remain authoritative.
public sealed class MiniAppCatalogPublication : CreatedEntity
{
    public Guid PublicationId { get; set; }
    public Guid MiniAppId { get; set; }
    public Guid MiniAppVersionId { get; set; }
    public Guid ApprovedReviewRequestId { get; set; }
    public string SnapshotHash { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? Description { get; set; }
    public string VersionName { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string Domain { get; set; } = "";
    public bool SsoEnabled { get; set; }
    public string? ProductionClientKey { get; set; }
    public string? ProductionClientType { get; set; }
    public string? ProductionScopes { get; set; }
    public DateTime PublishedAt { get; set; }
}
