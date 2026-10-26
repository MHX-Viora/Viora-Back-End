using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

// Shared database account/profile. Browser sessions and pairwise OAuth subjects remain separate.
public sealed class SandboxIdentityTemplate : AuditableEntity
{
    public static readonly Guid DefaultId = Guid.Parse("ac744f84-d1eb-4c32-a8a0-9ac94165be10");
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string FullName { get; set; } = "ANKT Developer Test";
    public string Nickname { get; set; } = "ankt_sandbox_test";
    public DateOnly Birthdate { get; set; } = new(2000, 1, 1);
}

public sealed class SandboxTestIdentity : CreatedEntity
{
    public Guid DeveloperAccountId { get; set; }
    public DeveloperAccount Developer { get; set; } = null!;
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

public sealed class SandboxIdentitySession : CreatedEntity
{
    public Guid IdentityId { get; set; }
    public SandboxTestIdentity Identity { get; set; } = null!;
    public Guid DeveloperSessionId { get; set; }
    public DeveloperSession DeveloperSession { get; set; } = null!;
    public Guid OAuthClientId { get; set; }
    public MiniAppOAuthClient OAuthClient { get; set; } = null!;
    public Guid ClientVersion { get; set; }
    public string LaunchHash { get; set; } = "";
    public DateTime LaunchExpiresAt { get; set; }
    public DateTime? LaunchConsumedAt { get; set; }
    public string? BrowserHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? TokenIssuedAt { get; set; }
    public DateTime? UserInfoAt { get; set; }
}
