using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class LiveCategoryConfiguration : IEntityTypeConfiguration<LiveCategory>
{
    public void Configure(EntityTypeBuilder<LiveCategory> builder)
    {
        builder.ToTable("LiveCategories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Icon).HasMaxLength(80);
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

internal sealed class LiveConfiguration : IEntityTypeConfiguration<Live>
{
    public void Configure(EntityTypeBuilder<Live> builder)
    {
        builder.ToTable("Lives");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CoverUrl).HasMaxLength(2048);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.AgoraChannelName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NextAgoraUid).HasDefaultValue(1L);
        builder.HasIndex(x => x.AgoraChannelName).IsUnique();
        builder.HasIndex(x => new { x.Status, x.StartedAt });
        builder.HasIndex(x => new { x.HostUserId, x.CreatedAt });
        builder.HasIndex(x => x.CategoryId);
        builder.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveAgoraParticipantConfiguration : IEntityTypeConfiguration<LiveAgoraParticipant>
{
    public void Configure(EntityTypeBuilder<LiveAgoraParticipant> builder)
    {
        builder.ToTable("LiveAgoraParticipants");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.LiveId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.LiveId, x.AgoraUid }).IsUnique();
        builder.HasOne<Live>().WithMany().HasForeignKey(x => x.LiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveGiftConfiguration : IEntityTypeConfiguration<LiveGift>
{
    public void Configure(EntityTypeBuilder<LiveGift> builder)
    {
        builder.ToTable("LiveGifts", table => table.HasCheckConstraint("CK_LiveGifts_PriceCoin", "\"PriceCoin\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.AnimationUrl).HasMaxLength(2048);
        builder.Property(x => x.EffectType).HasDefaultValue(LiveGiftEffectType.None);
        builder.Property(x => x.EffectTier).HasDefaultValue((short)0);
        builder.Property(x => x.EffectDurationMs).HasDefaultValue(0);
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

internal sealed class LiveViewerSessionConfiguration : IEntityTypeConfiguration<LiveViewerSession>
{
    public void Configure(EntityTypeBuilder<LiveViewerSession> builder)
    {
        builder.ToTable("LiveViewerSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConnectionId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.LiveId, x.UserId, x.JoinedAt });
        builder.HasIndex(x => new { x.LiveId, x.ConnectionId }).IsUnique().HasFilter("\"LeftAt\" IS NULL");
        builder.HasOne<Live>().WithMany().HasForeignKey(x => x.LiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveCommentConfiguration : IEntityTypeConfiguration<LiveComment>
{
    public void Configure(EntityTypeBuilder<LiveComment> builder)
    {
        builder.ToTable("LiveComments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.LiveId, x.CreatedAt });
        builder.HasOne<Live>().WithMany().HasForeignKey(x => x.LiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveGiftTransactionConfiguration : IEntityTypeConfiguration<LiveGiftTransaction>
{
    public void Configure(EntityTypeBuilder<LiveGiftTransaction> builder)
    {
        builder.ToTable("LiveGiftTransactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeePercent).HasPrecision(5, 2);
        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => new { x.LiveId, x.CreatedAt });
        builder.HasIndex(x => new { x.SenderUserId, x.CreatedAt });
        builder.HasIndex(x => new { x.HostUserId, x.CreatedAt });
        builder.HasOne<Live>().WithMany().HasForeignKey(x => x.LiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveGift>().WithMany().HasForeignKey(x => x.GiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WalletTransaction>().WithOne().HasForeignKey<LiveGiftTransaction>(x => x.WalletTransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveModeratorConfiguration : IEntityTypeConfiguration<LiveModerator>
{
    public void Configure(EntityTypeBuilder<LiveModerator> builder)
    {
        builder.ToTable("LiveModerators");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.LiveId, x.UserId }).IsUnique();
        builder.HasOne<Live>().WithMany().HasForeignKey(x => x.LiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveUserRestrictionConfiguration : IEntityTypeConfiguration<LiveUserRestriction>
{
    public void Configure(EntityTypeBuilder<LiveUserRestriction> builder)
    {
        builder.ToTable("LiveUserRestrictions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.LiveId, x.UserId }).IsUnique();
        builder.HasOne<Live>().WithMany().HasForeignKey(x => x.LiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AppliedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
