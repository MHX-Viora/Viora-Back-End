using Viora.Domain.Developers;

namespace Viora.Application.Developers;

public sealed record MiniAppReviewAuditActor(MiniAppReviewActorType Type, Guid Id);
public sealed record MiniAppReviewAuditEntry
{
    public required Guid MiniAppId { get; init; }
    public Guid? VersionId { get; init; }
    public Guid? ReviewId { get; init; }
    public required MiniAppReviewAuditActor Actor { get; init; }
    public required MiniAppReviewActionKind Action { get; init; }
    public required Guid RequestId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required string PayloadHash { get; init; }
    public string? BeforeState { get; init; }
    public string? AfterState { get; init; }
    public string? Reason { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public string ResultJson { get; init; } = "{}";
}

public sealed record MiniAppReviewChangeItem(string Field, string Message);
public sealed record MiniAppReviewSubmitRequest(Guid RequestId, Guid VersionId, Guid ConcurrencyToken, Guid AppVersion);
public sealed record MiniAppReviewCommand(Guid RequestId, Guid ConcurrencyToken);
public sealed record MiniAppReviewDecisionRequest(Guid RequestId, Guid ConcurrencyToken, string? Reason,
    IReadOnlyList<MiniAppReviewChangeItem>? Changes = null);
public sealed record MiniAppReviewAssignmentRequest(Guid RequestId, Guid ConcurrencyToken, Guid AdminAccountId, string? Reason);
public sealed record MiniAppReviewSummaryDto(Guid Id, Guid VersionId, long VersionCode, string Status,
    DateTime SubmittedAt, DateTime? ReviewStartedAt, DateTime? DecidedAt, Guid ConcurrencyToken);
public sealed record MiniAppReviewActionDto(string Action, string ActorType, string? BeforeState, string? AfterState,
    string? Reason, DateTime CreatedAt);
public sealed record MiniAppReviewDecisionDto(Guid ReviewId, string Status, Guid ConcurrencyToken, DateTime DecidedAt);
public sealed record MiniAppReviewPageDto(IReadOnlyList<MiniAppReviewSummaryDto> Items, int Page, int PageSize, int Total);
public sealed record MiniAppReviewDetailDto(MiniAppReviewSummaryDto Review, string VersionName, string SnapshotHash,
    MiniAppVersionSnapshot Snapshot, MiniAppReviewEvidenceAttestation Evidence, string? Reason,
    IReadOnlyList<MiniAppReviewChangeItem> Changes, IReadOnlyList<MiniAppReviewActionDto> Actions);
