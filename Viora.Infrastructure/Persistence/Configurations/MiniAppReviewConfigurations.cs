using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppReviewRequestConfiguration : IEntityTypeConfiguration<MiniAppReviewRequest>
{
    public void Configure(EntityTypeBuilder<MiniAppReviewRequest> b)
    {
        b.ToTable("MiniAppReviewRequests", t =>
        {
            t.HasCheckConstraint("CK_MiniAppReviews_Status", "\"Status\" IN (0,1,2,3,4)");
            t.HasCheckConstraint("CK_MiniAppReviews_Assignment", "(\"Status\" = 0 AND \"AssignedAdminAccountId\" IS NULL AND \"ReviewStartedAt\" IS NULL) OR (\"Status\" <> 0 AND \"AssignedAdminAccountId\" IS NOT NULL AND \"ReviewStartedAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_MiniAppReviews_Decision", "(\"Status\" IN (0,1) AND \"DecidedAt\" IS NULL) OR (\"Status\" IN (2,3,4) AND \"DecidedAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_MiniAppReviews_Reason", "\"Status\" NOT IN (2,3) OR length(trim(\"DecisionReason\")) BETWEEN 10 AND 2000 AND \"DecisionReason\" IS NOT NULL");
            t.HasCheckConstraint("CK_MiniAppReviews_Evidence", "length(\"EvidenceAttestationJson\") BETWEEN 2 AND 32768");
            t.HasCheckConstraint("CK_MiniAppReviews_Changes", "length(\"ChangeRequestsJson\") BETWEEN 2 AND 16384");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.MiniAppId });
        b.HasAlternateKey(x => new { x.Id, x.MiniAppId, x.MiniAppVersionId });
        b.Property(x => x.DecisionReason).HasMaxLength(2000);
        b.Property(x => x.ChangeRequestsJson).HasMaxLength(16384).IsRequired();
        b.Property(x => x.EvidenceAttestationJson).HasMaxLength(32768).IsRequired();
        b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.HasIndex(x => x.MiniAppId).IsUnique().HasFilter("\"Status\" IN (0,1)");
        b.HasIndex(x => x.MiniAppVersionId).IsUnique().HasFilter("\"Status\" IN (0,1)");
        b.HasIndex(x => new { x.Status, x.SubmittedAt });
        b.HasIndex(x => new { x.AssignedAdminAccountId, x.Status, x.SubmittedAt });
        b.HasOne<MiniAppVersion>().WithMany().HasForeignKey(x => new { x.MiniAppVersionId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AssignedAdminAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppReviewActionConfiguration : IEntityTypeConfiguration<MiniAppReviewAction>
{
    public void Configure(EntityTypeBuilder<MiniAppReviewAction> b)
    {
        b.ToTable("MiniAppReviewActions", t =>
        {
            t.HasCheckConstraint("CK_MiniAppReviewActions_Actor", "(\"ActorType\" = 0 AND \"ActorDeveloperId\" IS NOT NULL AND \"ActorAdminAccountId\" IS NULL AND \"ActorId\" = \"ActorDeveloperId\") OR (\"ActorType\" = 1 AND \"ActorAdminAccountId\" IS NOT NULL AND \"ActorDeveloperId\" IS NULL AND \"ActorId\" = \"ActorAdminAccountId\")");
            t.HasCheckConstraint("CK_MiniAppReviewActions_Action", "\"Action\" BETWEEN 0 AND 13");
            t.HasCheckConstraint("CK_MiniAppReviewActions_Hash", "length(\"PayloadHash\") = 64");
            t.HasCheckConstraint("CK_MiniAppReviewActions_Payload", "length(\"MetadataJson\") BETWEEN 2 AND 2048 AND length(\"ResultJson\") BETWEEN 2 AND 4096");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.MetadataJson).HasMaxLength(2048).IsRequired();
        b.Property(x => x.ResultJson).HasMaxLength(4096).IsRequired();
        b.Property(x => x.BeforeState).HasMaxLength(32); b.Property(x => x.AfterState).HasMaxLength(32);
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasIndex(x => new { x.ActorType, x.ActorId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.MiniAppId, x.CreatedAt });
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppReviewRequest>().WithMany().HasForeignKey(x => new { x.ReviewRequestId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppVersion>().WithMany().HasForeignKey(x => new { x.MiniAppVersionId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x => x.ActorDeveloperId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAdminAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
