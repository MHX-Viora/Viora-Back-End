using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class FeaturedBrandConfiguration : IEntityTypeConfiguration<FeaturedBrand>
{
    public void Configure(EntityTypeBuilder<FeaturedBrand> builder)
    {
        builder.ToTable("FeaturedBrands");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.LogoUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}
