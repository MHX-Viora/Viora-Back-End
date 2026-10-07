using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class AdvertisementConfiguration : IEntityTypeConfiguration<Advertisement>
{
    public void Configure(EntityTypeBuilder<Advertisement> builder)
    {
        builder.ToTable("Advertisements", table =>
        {
            table.HasCheckConstraint("CK_Advertisements_Budget", "\"TotalBudget\" >= 50000 AND \"SpentAmount\" >= 0 AND \"ReservedAmount\" >= 0 AND \"SpentAmount\" + \"ReservedAmount\" <= \"TotalBudget\"");
            table.HasCheckConstraint("CK_Advertisements_Schedule", "\"EndAt\" > \"StartAt\"");
            table.HasCheckConstraint("CK_Advertisements_Ages", "(\"MinimumAge\" IS NULL OR \"MinimumAge\" >= 13) AND (\"MaximumAge\" IS NULL OR \"MaximumAge\" <= 100) AND (\"MinimumAge\" IS NULL OR \"MaximumAge\" IS NULL OR \"MaximumAge\" >= \"MinimumAge\")");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DestinationUrl).HasMaxLength(2048);
        builder.Property(x => x.TargetLocation).HasMaxLength(120);
        builder.Property(x => x.ReviewReason).HasMaxLength(500);
        builder.Property(x => x.DailyBudget).HasPrecision(18, 2);
        builder.Property(x => x.TotalBudget).HasPrecision(18, 2);
        builder.Property(x => x.SpentAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReservedAmount).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.Status, x.Placement, x.StartAt, x.EndAt });
        builder.HasIndex(x => new { x.AdvertiserId, x.CreatedAt });
        builder.HasIndex(x => x.PostId);
        builder.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Advertiser).WithMany().HasForeignKey(x => x.AdvertiserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdvertisementEventConfiguration : IEntityTypeConfiguration<AdvertisementEvent>
{
    public void Configure(EntityTypeBuilder<AdvertisementEvent> builder)
    {
        builder.ToTable("AdvertisementEvents", table => table.HasCheckConstraint("CK_AdvertisementEvents_Charge", "\"ChargeAmount\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ClientEventId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ChargeAmount).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.AdvertisementId, x.ViewerId, x.Type, x.ClientEventId }).IsUnique();
        builder.HasIndex(x => new { x.AdvertisementId, x.Type, x.CreatedAt });
        builder.HasOne(x => x.Advertisement).WithMany(x => x.Events).HasForeignKey(x => x.AdvertisementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Viewer).WithMany().HasForeignKey(x => x.ViewerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdvertisementFeedbackConfiguration : IEntityTypeConfiguration<AdvertisementFeedback>
{
    public void Configure(EntityTypeBuilder<AdvertisementFeedback> builder)
    {
        builder.ToTable("AdvertisementFeedback");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.AdvertisementId, x.ViewerId, x.Type }).IsUnique();
        builder.HasOne(x => x.Advertisement).WithMany(x => x.Feedback).HasForeignKey(x => x.AdvertisementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Viewer).WithMany().HasForeignKey(x => x.ViewerId).OnDelete(DeleteBehavior.Restrict);
    }
}
