using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("LegalDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.LanguageCode).HasMaxLength(10).HasDefaultValue("vi").IsRequired();
        builder.Property(x => x.Version).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsPublished).HasDefaultValue(false);
        builder.HasIndex(x => new { x.Type, x.LanguageCode, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.Type, x.LanguageCode })
            .HasFilter("\"IsPublished\" = TRUE")
            .IsUnique();
    }
}

internal sealed class UserLegalAcceptanceConfiguration : IEntityTypeConfiguration<UserLegalAcceptance>
{
    public void Configure(EntityTypeBuilder<UserLegalAcceptance> builder)
    {
        builder.ToTable("UserLegalAcceptances");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).HasMaxLength(20).IsRequired();
        builder.Property(x => x.AppVersion).HasMaxLength(30);
        builder.Property(x => x.IpAddress).HasMaxLength(100);
        builder.HasIndex(x => new { x.UserId, x.LegalDocumentId, x.Version }).IsUnique();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LegalDocument).WithMany().HasForeignKey(x => x.LegalDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
