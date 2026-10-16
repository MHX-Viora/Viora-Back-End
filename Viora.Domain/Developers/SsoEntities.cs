using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public sealed class AnktSsoSession : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid SocialSessionId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
