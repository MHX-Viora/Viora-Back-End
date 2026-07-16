namespace Viora.Domain.Entities;

public sealed class DeviceToken : AuditableEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = null!;
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public DevicePlatform Platform { get; set; }
    public string? AppVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSeenAt { get; set; }
    public User User { get; set; } = null!;
}
