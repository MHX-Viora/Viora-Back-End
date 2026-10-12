using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppFavoriteConfiguration : IEntityTypeConfiguration<MiniAppFavorite>
{
    public void Configure(EntityTypeBuilder<MiniAppFavorite> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.AccountId, x.MiniAppId }).IsUnique();
        builder.HasIndex(x => new { x.AccountId, x.SortOrder });
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MiniAppHistoryStateConfiguration : IEntityTypeConfiguration<MiniAppHistoryState>
{
    public void Configure(EntityTypeBuilder<MiniAppHistoryState> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.AccountId).IsUnique();
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MiniAppBannerConfiguration : IEntityTypeConfiguration<MiniAppBanner>
{
    public void Configure(EntityTypeBuilder<MiniAppBanner> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(160);
        builder.Property(x => x.ImageUrl).HasMaxLength(2048);
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MiniAppCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_MiniAppBanner_Target", "(\"MiniAppId\" IS NULL) <> (\"CategoryId\" IS NULL)"));
    }
}

internal sealed class MiniAppPlacementConfiguration : IEntityTypeConfiguration<MiniAppPlacement>
{
    public void Configure(EntityTypeBuilder<MiniAppPlacement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.MiniAppId).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
    }
}
