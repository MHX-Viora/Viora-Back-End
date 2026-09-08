using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class StickerPackConfiguration : IEntityTypeConfiguration<StickerPack>
{
    public void Configure(EntityTypeBuilder<StickerPack> builder)
    {
        builder.ToTable("StickerPacks", table => table.HasCheckConstraint(
            "CK_StickerPacks_PriceAndAvailability",
            "\"Price\" >= 0 AND (\"AvailableUntil\" IS NULL OR \"AvailableFrom\" IS NULL OR \"AvailableUntil\" > \"AvailableFrom\")"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsFeatured).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.HasIndex(x => new { x.IsActive, x.IsFeatured, x.SortOrder });
        builder.HasIndex(x => x.Price);
    }
}

internal sealed class StickerConfiguration : IEntityTypeConfiguration<Sticker>
{
    public void Configure(EntityTypeBuilder<Sticker> builder)
    {
        builder.ToTable("Stickers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.StickerPackId, x.IsActive, x.SortOrder });
        builder.HasOne(x => x.StickerPack).WithMany(x => x.Stickers).HasForeignKey(x => x.StickerPackId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserStickerPackConfiguration : IEntityTypeConfiguration<UserStickerPack>
{
    public void Configure(EntityTypeBuilder<UserStickerPack> builder)
    {
        builder.ToTable("UserStickerPacks", table => table.HasCheckConstraint("CK_UserStickerPacks_PurchasePrice", "\"PurchasePrice\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PurchasePrice).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.UserId, x.StickerPackId }).IsUnique();
        builder.HasIndex(x => x.StickerPackId);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StickerPack).WithMany(x => x.Owners).HasForeignKey(x => x.StickerPackId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StickerPackPurchaseConfiguration : IEntityTypeConfiguration<StickerPackPurchase>
{
    public void Configure(EntityTypeBuilder<StickerPackPurchase> builder)
    {
        builder.ToTable("StickerPackPurchases", table => table.HasCheckConstraint("CK_StickerPackPurchases_Price", "\"Price\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.UserId, x.StickerPackId, x.CreatedAt });
        builder.HasIndex(x => x.StickerPackId);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StickerPack).WithMany().HasForeignKey(x => x.StickerPackId).OnDelete(DeleteBehavior.Restrict);
    }
}
