namespace Viora.Application.Developers;

public sealed record MiniAppPublishRequest(Guid RequestId, Guid ConcurrencyToken, Guid VersionId,
    Guid? PublicationId = null, Guid? PublicationRevision = null, Guid? OperationalRevision = null);
public sealed record MiniAppUnpublishRequest(Guid RequestId, Guid ConcurrencyToken, string? Reason,
    Guid PublicationId, Guid PublicationRevision, Guid? OperationalRevision = null);
public sealed record MiniAppModerationRequest(Guid RequestId, Guid ConcurrencyToken, string Reason,
    Guid? OperationalRevision = null, Guid? PublicationId = null, Guid? PublicationRevision = null);
public sealed record MiniAppModerationResultDto(MiniAppOperationalDto Operational, Guid AppVersion, MiniAppPublicationDto? Publication);
public sealed record MiniAppPublicationDto(Guid Id, Guid VersionId, long VersionCode, Guid ApprovedReviewRequestId,
    string Status, DateTime PublishedAt, DateTime? UnpublishedAt, Guid ConcurrencyToken);
public sealed record MiniAppOperationalDto(string Status, string? Reason, DateTime? SuspendedAt, Guid ConcurrencyToken);
public sealed record MiniAppPublicationReadinessDto(Guid VersionId, string SnapshotHash, Guid? ApprovedReviewRequestId,
    Guid AppVersion, Guid? PublicationId, Guid? PublicationRevision, Guid? OperationalRevision,
    bool CanPublish, bool CanUnpublish, DateTime EvaluatedAt, string[] BlockingReasons);
public sealed record MiniAppPublishResultDto(MiniAppPublicationDto Publication, Guid AppVersion, Guid OperationalRevision);
public sealed record MiniAppPublicationPageDto(IReadOnlyList<MiniAppPublicationDto> Items, int Page, int PageSize, int Total);
