using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("DeviceTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Token).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(255);
        builder.Property(x => x.DeviceName).HasMaxLength(255);
        builder.Property(x => x.AppVersion).HasMaxLength(50);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => x.DeviceId).IsUnique().HasFilter("\"DeviceId\" IS NOT NULL");
        builder.HasIndex(x => new { x.UserId, x.IsActive });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
