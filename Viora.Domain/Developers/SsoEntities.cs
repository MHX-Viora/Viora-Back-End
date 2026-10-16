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

public sealed class SsoLoginTransaction : CreatedEntity
{
    public string BrowserHash { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
    public Guid? AccountId { get; set; }
    public Guid? SocialSessionId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
