using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum MiniAppPublicationStatus { Published, UnpublishedByOwner, Archived, Suspended }
public enum MiniAppOperationalStatus { Active, Suspended }

public sealed class MiniAppPublication : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid MiniAppVersionId { get; set; }
    public Guid ApprovedReviewRequestId { get; set; }
    public Guid? ProductionOAuthClientId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; } = MiniAppOAuthEnvironment.Production;
    public MiniAppPublicationStatus Status { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime? UnpublishedAt { get; set; }
    public Guid PublishedByDeveloperId { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public string? SuspensionReason { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class MiniAppOperationalState : AuditableEntity
{
    public Guid MiniAppId { get; set; }
    public MiniAppOperationalStatus Status { get; set; }
    public string? Reason { get; set; }
    public Guid? ChangedByAdminAccountId { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
