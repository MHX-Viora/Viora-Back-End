namespace Viora.Domain.Entities;

public sealed class MiniAppMembership : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid AccountId { get; set; }
    public DeveloperMembershipRole Role { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public Account Account { get; set; } = null!;
}

// Invitations bind an existing account ID, never a client-asserted email address.
public sealed class MiniAppMemberInvitation : CreatedEntity
{
    public Guid MiniAppId { get; set; }
    public Guid AccountId { get; set; }
    public Guid InvitedByAccountId { get; set; }
    public DeveloperMembershipRole Role { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public Account Account { get; set; } = null!;
}
