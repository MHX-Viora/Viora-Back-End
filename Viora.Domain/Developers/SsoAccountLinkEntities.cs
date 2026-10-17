using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public sealed class SsoAccountLinkChallenge : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public Guid OAuthClientId { get; set; }
    public Guid ClientSecretId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; }
    public string Subject { get; set; } = "";
    public string PartnerAccountId { get; set; } = "";
    public string ProofHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public sealed class MiniAppAccountLink : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; }
    public string Subject { get; set; } = "";
    public string PartnerAccountId { get; set; } = "";
    public DateTime LinkedAt { get; set; }
    public DateTime? UnlinkedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class SsoAccountLinkAudit : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; }
    public Guid LinkId { get; set; }
    public string Action { get; set; } = "";
}
