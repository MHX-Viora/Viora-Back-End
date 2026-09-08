namespace Viora.Domain.Entities;

public sealed class StickerPack : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string ThumbnailUrl { get; set; } = null!;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableUntil { get; set; }
    public ICollection<Sticker> Stickers { get; set; } = [];
    public ICollection<UserStickerPack> Owners { get; set; } = [];
}

public sealed class Sticker : AuditableEntity
{
    public Guid StickerPackId { get; set; }
    public string Name { get; set; } = null!;
    public string ImageUrl { get; set; } = null!;
    public string? ThumbnailUrl { get; set; }
    public StickerFormat Format { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public StickerPack StickerPack { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
}

public sealed class UserStickerPack : CreatedEntity
{
    public Guid UserId { get; set; }
    public Guid StickerPackId { get; set; }
    public decimal PurchasePrice { get; set; }
    public Guid? TransactionId { get; set; }
    public DateTime PurchasedAt { get; set; }
    public User User { get; set; } = null!;
    public StickerPack StickerPack { get; set; } = null!;
}

public sealed class StickerPackPurchase : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid StickerPackId { get; set; }
    public decimal Price { get; set; }
    public Guid? TransactionId { get; set; }
    public StickerPurchaseStatus Status { get; set; }
    public DateTime? CompletedAt { get; set; }
    public User User { get; set; } = null!;
    public StickerPack StickerPack { get; set; } = null!;
}
