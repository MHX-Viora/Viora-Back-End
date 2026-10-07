using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class WithdrawalFeeSettingsConfiguration : IEntityTypeConfiguration<WithdrawalFeeSettings>
{
    public void Configure(EntityTypeBuilder<WithdrawalFeeSettings> builder)
    {
        builder.ToTable("WithdrawalFeeSettings", table =>
        {
            table.HasCheckConstraint("CK_WithdrawalFeeSettings_Singleton", "\"Id\" = 1");
            table.HasCheckConstraint("CK_WithdrawalFeeSettings_Rate", "\"FeePercent\" >= 0 AND \"FeePercent\" <= 99.99");
            table.HasCheckConstraint("CK_WithdrawalFeeSettings_Version", "\"Version\" > 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.FeePercent).HasPrecision(5, 2);
        builder.Property(item => item.Version).IsConcurrencyToken();
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasData(new WithdrawalFeeSettings { Id = 1, FeePercent = 10m, Version = 1, UpdatedAt = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc) });
    }
}
