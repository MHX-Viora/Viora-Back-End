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

public sealed class MiniAppPairwiseSubject : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; }
    public string Subject { get; set; } = "";
}

public sealed class MiniAppUserConsent : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public Guid OAuthClientId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; }
    public string? AuthorizationId { get; set; }
    public string GrantedScopes { get; set; } = "";
    public DateTime GrantedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
