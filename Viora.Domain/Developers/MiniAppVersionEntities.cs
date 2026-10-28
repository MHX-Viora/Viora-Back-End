using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum MiniAppVersionState { Draft, Submitted, Archived }

public sealed class MiniAppVersion : AuditableEntity
{
    public Guid MiniAppId { get; set; }
    public long VersionCode { get; set; }
    public string VersionName { get; set; } = "";
    public MiniAppVersionState State { get; set; }
    public int SnapshotSchemaVersion { get; set; } = 1;
    public string SnapshotJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
    public Guid CreatedByDeveloperId { get; set; }
    public Guid? LogoUploadId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
