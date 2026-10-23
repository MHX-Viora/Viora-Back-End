using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppVersionConfiguration : IEntityTypeConfiguration<MiniAppVersion>
{
    public void Configure(EntityTypeBuilder<MiniAppVersion> b)
    {
        b.ToTable("MiniAppVersions", t =>
        {
            t.HasCheckConstraint("CK_MiniAppVersions_Code", "\"VersionCode\" > 0");
            t.HasCheckConstraint("CK_MiniAppVersions_State", "\"State\" IN (0,1,2)");
            t.HasCheckConstraint("CK_MiniAppVersions_Schema", "\"SnapshotSchemaVersion\" = 1");
            t.HasCheckConstraint("CK_MiniAppVersions_Hash", "length(\"SnapshotHash\") = 64");
            t.HasCheckConstraint("CK_MiniAppVersions_Snapshot", "length(\"SnapshotJson\") BETWEEN 2 AND 65536");
            t.HasCheckConstraint("CK_MiniAppVersions_Submitted", "(\"State\" = 0 AND \"SubmittedAt\" IS NULL) OR (\"State\" IN (1,2) AND \"SubmittedAt\" IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.MiniAppId });
        b.Property(x => x.VersionName).HasMaxLength(80).IsRequired();
        b.Property(x => x.SnapshotJson).HasMaxLength(65536).IsRequired();
        b.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.HasIndex(x => new { x.MiniAppId, x.VersionCode }).IsUnique();
        b.HasIndex(x => x.MiniAppId).IsUnique().HasFilter("\"State\" = 0");
        b.HasIndex(x => new { x.MiniAppId, x.CreatedAt });
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x => x.CreatedByDeveloperId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppLogoUpload>().WithMany().HasForeignKey(x => x.LogoUploadId).OnDelete(DeleteBehavior.Restrict);
    }
}
