using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public sealed class MiniAppBridgeSession : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid ActorId { get; set; }
    public Guid SourceSessionId { get; set; }
    public bool IsSandbox { get; set; }
    public string ClientId { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Policy { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public DateTime CodeExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? AccessHash { get; set; }
    public DateTime? AccessExpiresAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
