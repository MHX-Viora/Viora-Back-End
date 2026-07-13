namespace Viora.Domain.Entities;

public sealed class Follow : CreatedEntity
{
    public Guid FollowerId { get; set; }
    public Guid FollowingId { get; set; }
    public User Follower { get; set; } = null!;
    public User Following { get; set; } = null!;
}

public sealed class Friendship : AuditableEntity
{
    public Guid RequesterUserId { get; set; }
    public Guid AddresseeUserId { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTime? RespondedAt { get; set; }
    public User RequesterUser { get; set; } = null!;
    public User AddresseeUser { get; set; } = null!;
}
