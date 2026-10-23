using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum MiniAppReviewStatus { PendingReview, InReview, ChangesRequested, Rejected, Approved }
public enum MiniAppReviewActorType { Developer, Admin }
public enum MiniAppReviewActionKind
{
    VersionCreated, VersionUpdated, ReviewSubmitted, ReviewStarted, ReviewAssigned,
    ChangesRequested, ReviewRejected, ReviewApproved, Published, Unpublished, Suspended,
    Restored, CredentialPrepared, NotificationRead
}

public sealed class MiniAppReviewRequest : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid MiniAppVersionId { get; set; }
    public Guid DeveloperAccountId { get; set; }
    public MiniAppReviewStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public Guid? AssignedAdminAccountId { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public string ChangeRequestsJson { get; set; } = "[]";
    public string EvidenceAttestationJson { get; set; } = "";
    public Guid CorrelationId { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class MiniAppReviewAction : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public Guid? MiniAppVersionId { get; set; }
    public MiniAppReviewActorType ActorType { get; set; }
    public Guid ActorId { get; set; }
    public Guid? ActorDeveloperId { get; set; }
    public Guid? ActorAdminAccountId { get; set; }
    public MiniAppReviewActionKind Action { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? Reason { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public Guid CorrelationId { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
