using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppReviewNotificationConfiguration : IEntityTypeConfiguration<MiniAppReviewNotification>
{
    public void Configure(EntityTypeBuilder<MiniAppReviewNotification> b)
    {
        b.ToTable("MiniAppReviewNotifications", t =>
        {
            t.HasCheckConstraint("CK_MiniAppReviewNotifications_Event", "\"EventType\" BETWEEN 0 AND 8");
            t.HasCheckConstraint("CK_MiniAppReviewNotifications_Read", "\"ReadAt\" IS NULL OR \"ReadAt\" >= \"CreatedAt\"");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.EventId, x.RecipientDeveloperId });
        b.HasIndex(x => new { x.EventId, x.RecipientDeveloperId }).IsUnique();
        b.HasIndex(x => new { x.RecipientDeveloperId, x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.RecipientDeveloperId, x.ReadAt });
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x => x.RecipientDeveloperId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => new { x.MiniAppId, x.RecipientDeveloperId })
            .HasPrincipalKey(x => new { x.Id, x.DeveloperAccountId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppReviewAction>().WithMany().HasForeignKey(x => new { x.EventId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppReviewRequest>().WithMany().HasForeignKey(x => new { x.ReviewRequestId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppVersion>().WithMany().HasForeignKey(x => new { x.MiniAppVersionId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppPublication>().WithMany().HasForeignKey(x => new { x.PublicationId, x.MiniAppId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppReviewNotificationDeliveryConfiguration : IEntityTypeConfiguration<MiniAppReviewNotificationDelivery>
{
    public void Configure(EntityTypeBuilder<MiniAppReviewNotificationDelivery> b)
    {
        b.ToTable("MiniAppReviewNotificationDeliveries", t =>
        {
            t.HasCheckConstraint("CK_MiniAppReviewDeliveries_Channel", "\"Channel\" = 0");
            t.HasCheckConstraint("CK_MiniAppReviewDeliveries_Status", "\"Status\" BETWEEN 0 AND 3");
            t.HasCheckConstraint("CK_MiniAppReviewDeliveries_Attempts", "\"AttemptCount\" BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_MiniAppReviewDeliveries_Error", "\"LastErrorCode\" BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_MiniAppReviewDeliveries_Lease", "(\"Status\" = 1 AND \"LeaseId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 1 AND \"LeaseId\" IS NULL AND \"LeaseUntil\" IS NULL)");
            t.HasCheckConstraint("CK_MiniAppReviewDeliveries_Sent", "(\"Status\" = 2 AND \"SentAt\" IS NOT NULL) OR (\"Status\" <> 2 AND \"SentAt\" IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EventId, x.RecipientDeveloperId, x.Channel }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => new { x.Status, x.LeaseUntil });
        b.HasOne<MiniAppReviewNotification>().WithMany().HasForeignKey(x => new { x.NotificationId, x.EventId, x.RecipientDeveloperId })
            .HasPrincipalKey(x => new { x.Id, x.EventId, x.RecipientDeveloperId }).OnDelete(DeleteBehavior.Restrict);
    }
}
