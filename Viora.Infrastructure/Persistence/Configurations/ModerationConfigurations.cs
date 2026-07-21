using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.IsRead).HasDefaultValue(false);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SenderUser).WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasDefaultValue(ReportReason.Other);
        builder.Property(x => x.Status).HasDefaultValue(ReportStatus.Pending);
        builder.HasOne(x => x.ReporterUser).WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdminLogConfiguration : IEntityTypeConfiguration<AdminLog>
{
    public void Configure(EntityTypeBuilder<AdminLog> builder)
    {
        builder.ToTable("AdminLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.AdminId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
    }
}
