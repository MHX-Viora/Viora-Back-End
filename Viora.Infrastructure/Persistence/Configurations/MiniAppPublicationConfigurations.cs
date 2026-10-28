using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppPublicationConfiguration : IEntityTypeConfiguration<MiniAppPublication>
{
    public void Configure(EntityTypeBuilder<MiniAppPublication> b)
    {
        b.ToTable("MiniAppPublications", t =>
        {
            t.HasCheckConstraint("CK_MiniAppPublications_Environment", "\"Environment\" = 1");
            t.HasCheckConstraint("CK_MiniAppPublications_Status", "\"Status\" IN (0,1,2,3)");
            t.HasCheckConstraint("CK_MiniAppPublications_Closure", "(\"Status\" = 0 AND \"UnpublishedAt\" IS NULL) OR (\"Status\" <> 0 AND \"UnpublishedAt\" IS NOT NULL AND \"UnpublishedAt\" >= \"PublishedAt\")");
            t.HasCheckConstraint("CK_MiniAppPublications_Suspension", "\"Status\" <> 3 OR (\"SuspendedAt\" IS NOT NULL AND \"SuspensionReason\" IS NOT NULL AND length(trim(\"SuspensionReason\")) BETWEEN 10 AND 2000)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.SuspensionReason).HasMaxLength(2000);
        b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.HasIndex(x => x.MiniAppId).IsUnique().HasFilter("\"Status\" = 0");
        b.HasIndex(x => new { x.MiniAppId, x.PublishedAt });
        b.HasOne<MiniAppVersion>().WithMany().HasForeignKey(x => new { x.MiniAppVersionId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppReviewRequest>().WithMany().HasForeignKey(x => new { x.ApprovedReviewRequestId, x.MiniAppId, x.MiniAppVersionId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId, x.MiniAppVersionId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppOAuthClient>().WithMany().HasForeignKey(x => new { x.ProductionOAuthClientId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x => x.PublishedByDeveloperId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppOperationalStateConfiguration : IEntityTypeConfiguration<MiniAppOperationalState>
{
    public void Configure(EntityTypeBuilder<MiniAppOperationalState> b)
    {
        b.ToTable("MiniAppOperationalStates", t =>
        {
            t.HasCheckConstraint("CK_MiniAppOperationalStates_Status", "\"Status\" IN (0,1)");
            t.HasCheckConstraint("CK_MiniAppOperationalStates_Suspension", "\"Status\" = 0 OR (\"ChangedByAdminAccountId\" IS NOT NULL AND \"SuspendedAt\" IS NOT NULL AND \"Reason\" IS NOT NULL AND length(trim(\"Reason\")) BETWEEN 10 AND 2000)");
        });
        b.HasKey(x => x.Id); b.HasIndex(x => x.MiniAppId).IsUnique();
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ChangedByAdminAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
