using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public sealed class MiniAppPartnerCredential : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public MiniAppOAuthEnvironment Environment { get; set; }
    public string SecretHash { get; set; } = "";
    public string SecretEncrypted { get; set; } = "";
    public string SecretKeyVersion { get; set; } = "";
    public DateTime? RevokedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
