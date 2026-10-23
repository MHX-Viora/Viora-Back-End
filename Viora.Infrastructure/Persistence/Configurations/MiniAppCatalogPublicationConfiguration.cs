using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppCatalogPublicationConfiguration : IEntityTypeConfiguration<MiniAppCatalogPublication>
{
    public void Configure(EntityTypeBuilder<MiniAppCatalogPublication> b)
    {
        b.ToTable("MiniAppCatalogPublications", t =>
        {
            t.HasCheckConstraint("CK_MiniAppCatalogPublications_Name", "length(trim(\"Name\")) BETWEEN 1 AND 120");
            t.HasCheckConstraint("CK_MiniAppCatalogPublications_Hash", "length(\"SnapshotHash\") = 64 AND \"SnapshotHash\" = lower(\"SnapshotHash\")");
            t.HasCheckConstraint("CK_MiniAppCatalogPublications_Description", "\"Description\" IS NULL OR length(\"Description\") <= 300");
            t.HasCheckConstraint("CK_MiniAppCatalogPublications_Version", "length(trim(\"VersionName\")) BETWEEN 1 AND 80");
            t.HasCheckConstraint("CK_MiniAppCatalogPublications_Domain", "length(\"Domain\") BETWEEN 1 AND 253 AND length(\"WebsiteUrl\") BETWEEN 1 AND 2048");
            t.HasCheckConstraint("CK_MiniAppCatalogPublications_Client", "(\"SsoEnabled\" AND \"ProductionClientKey\" IS NOT NULL AND length(\"ProductionClientKey\") BETWEEN 1 AND 100 AND \"ProductionClientType\" IS NOT NULL AND \"ProductionClientType\" IN ('Public','Confidential') AND \"ProductionScopes\" IS NOT NULL AND length(\"ProductionScopes\") BETWEEN 1 AND 64) OR (NOT \"SsoEnabled\" AND \"ProductionClientKey\" IS NULL AND \"ProductionClientType\" IS NULL AND \"ProductionScopes\" IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Category).HasMaxLength(50);
        b.Property(x => x.LogoUrl).HasMaxLength(2048);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.VersionName).HasMaxLength(80);
        b.Property(x => x.SnapshotHash).HasMaxLength(64);
        b.Property(x => x.WebsiteUrl).HasMaxLength(2048);
        b.Property(x => x.Domain).HasMaxLength(253);
        b.Property(x => x.ProductionClientKey).HasMaxLength(100);
        b.Property(x => x.ProductionClientType).HasMaxLength(20);
        b.Property(x => x.ProductionScopes).HasMaxLength(64);
        b.HasIndex(x => x.PublicationId).IsUnique();
        b.HasIndex(x => new { x.PublishedAt, x.PublicationId });
        b.HasOne<MiniAppPublication>().WithMany()
            .HasForeignKey(x => new { x.PublicationId, x.MiniAppId, x.MiniAppVersionId, x.ApprovedReviewRequestId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId, x.MiniAppVersionId, x.ApprovedReviewRequestId })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in b.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
