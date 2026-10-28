namespace Viora.Application.Developers;

public sealed record MiniAppReviewReadinessDto(Guid VersionId, string SnapshotHash, Guid ConcurrencyToken, Guid AppVersion,
    bool ReadyForReview, DateTime EvaluatedAt, Guid? SandboxRunId, string? ConfigurationFingerprint,
    SandboxReadinessCheck[] RequiredChecks, string[] BlockingReasons, string[] Warnings);
public sealed record MiniAppReviewEvidenceAttestation(int SchemaVersion, Guid VersionId, string SnapshotHash,
    Guid SandboxRunId, string ConfigurationFingerprint, DateTime EvaluatedAt, SandboxReadinessCheck[] Checks);
