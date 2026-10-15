namespace Viora.Domain.Entities;

public sealed class DeveloperReauth : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid? MiniAppId { get; set; }
    public Guid? DeveloperId { get; set; }
    public string Operation { get; set; } = null!;
    public string ProofHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? ProviderAuthenticationHash { get; set; }
}
