namespace Viora.Domain.Entities;

public enum LiveStatus : short
{
    Draft = 0, Preparing = 1, Live = 2, Reconnecting = 3,
    Ended = 4, Interrupted = 5, Cancelled = 6, Banned = 7
}

public enum LiveGiftAnimationType : short { Small = 0, Medium = 1, Fullscreen = 2 }
public enum LiveGiftEffectType : short { None = 0, Firework = 1, Rocket = 2, Crown = 3 }
public enum LivePrivacy : short { Public = 0, Followers = 1, Friends = 2 }

public sealed class LiveCategory : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Live : AuditableEntity
{
    public Guid HostUserId { get; set; }
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = null!;
    public string? CoverUrl { get; set; }
    public string? Description { get; set; }
    public LivePrivacy Privacy { get; set; } = LivePrivacy.Public;
    public bool AllowComments { get; set; } = true;
    public bool AllowGifts { get; set; } = true;
    public string AgoraChannelName { get; set; } = null!;
    public long NextAgoraUid { get; set; } = 1;
    public LiveStatus Status { get; set; } = LiveStatus.Preparing;
    public DateTime? StartedAt { get; set; }
    public DateTime? HostLastSeenAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int CurrentViewerCount { get; set; }
    public int PeakViewerCount { get; set; }
    public long TotalViews { get; set; }
    public int UniqueViewers { get; set; }
    public long TotalComments { get; set; }
    public long TotalReactions { get; set; }
    public long TotalGiftCount { get; set; }
    public long TotalGiftValue { get; set; }
    public User Host { get; set; } = null!;
    public LiveCategory Category { get; set; } = null!;
}

public sealed class LiveGift : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string ImageUrl { get; set; } = null!;
    public string? AnimationUrl { get; set; }
    public long PriceCoin { get; set; }
    public long Price { get; set; }
    public LiveGiftAnimationType AnimationType { get; set; }
    public LiveGiftEffectType EffectType { get; set; }
    public short EffectTier { get; set; }
    public int EffectDurationMs { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class LiveViewerSession : CreatedEntity
{
    public Guid LiveId { get; set; }
    public Guid UserId { get; set; }
    public string ConnectionId { get; set; } = null!;
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public int? DurationSeconds { get; set; }
}

public sealed class LiveAgoraParticipant : CreatedEntity
{
    public Guid LiveId { get; set; }
    public Guid UserId { get; set; }
    public long AgoraUid { get; set; }
}

public sealed class LiveComment : CreatedEntity
{
    public Guid LiveId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = null!;
    public bool IsPinned { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class LiveGiftTransaction : CreatedEntity
{
    public Guid RequestId { get; set; }
    public Guid LiveId { get; set; }
    public Guid GiftId { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid HostUserId { get; set; }
    public Guid WalletTransactionId { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceCoin { get; set; }
    public long TotalCoin { get; set; }
    public decimal FeePercent { get; set; }
    public long PlatformFee { get; set; }
    public long HostEarning { get; set; }
    // Nullable VND snapshots preserve historical coin transactions without revaluing them.
    public long? UnitPrice { get; set; }
    public long? GrossAmount { get; set; }
    public long? NetAmount { get; set; }
    public long? FeeAmount { get; set; }
    public Guid? ReceiverWalletTransactionId { get; set; }
    public string? GiftName { get; set; }
    public string Currency { get; set; } = "COIN";
    public WalletTransactionStatus Status { get; set; } = WalletTransactionStatus.Completed;
}

public sealed class LiveModerator : CreatedEntity
{
    public Guid LiveId { get; set; }
    public Guid UserId { get; set; }
    public Guid AssignedByUserId { get; set; }
}

public sealed class LiveUserRestriction : CreatedEntity
{
    public Guid LiveId { get; set; }
    public Guid UserId { get; set; }
    public bool IsMuted { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime? MutedUntil { get; set; }
    public Guid AppliedByUserId { get; set; }
}
