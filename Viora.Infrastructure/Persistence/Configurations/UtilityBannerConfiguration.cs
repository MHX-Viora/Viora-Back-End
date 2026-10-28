using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class UtilityBannerConfiguration : IEntityTypeConfiguration<UtilityBanner>
{
    public void Configure(EntityTypeBuilder<UtilityBanner> builder)
    {
        builder.ToTable("UtilityBanners");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.TargetUrl).HasMaxLength(2048);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}
