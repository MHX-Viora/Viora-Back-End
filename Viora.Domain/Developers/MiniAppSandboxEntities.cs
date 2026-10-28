using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public sealed class MiniAppSandboxSession : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid DeveloperAccountId { get; set; }
    public Guid OAuthClientId { get; set; }
    public MiniAppOAuthEnvironment Environment { get; set; } = MiniAppOAuthEnvironment.Sandbox;
    public string ActorType { get; set; } = "Developer";
    public Guid? ActorDeveloperAccountId { get; set; }
    public Guid? ActorAdminAccountId { get; set; }
    public Guid? SourceDeveloperSessionId { get; set; }
    public Guid? SourceSocialSessionId { get; set; }
    public string ConfigurationFingerprint { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string PartnerOrigin { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? RuntimeCredentialHash { get; set; }
    public int DroppedDiagnosticCount { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class MiniAppSandboxLaunchGrant : CreatedEntity
{
    public Guid SandboxSessionId { get; set; }
    public Guid? ExecutorRunId { get; set; }
    public Guid? ExecutorRunVersion { get; set; }
    public string GrantHash { get; set; } = "";
    public string BrowserNonceHash { get; set; } = "";
    public string Audience { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
