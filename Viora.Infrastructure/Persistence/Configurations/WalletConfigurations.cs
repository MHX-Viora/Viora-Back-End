using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets", table =>
        {
            table.HasCheckConstraint("CK_Wallets_AvailableBalance", "\"AvailableBalance\" >= 0");
            table.HasCheckConstraint("CK_Wallets_HeldBalance", "\"HeldBalance\" >= 0");
            table.HasCheckConstraint("CK_Wallets_Currency", "char_length(\"Currency\") = 3");
        });
        builder.HasKey(wallet => wallet.Id);
        builder.Property(wallet => wallet.AvailableBalance).HasPrecision(18, 2);
        builder.Property(wallet => wallet.HeldBalance).HasPrecision(18, 2);
        builder.Property(wallet => wallet.Currency).HasMaxLength(3).HasDefaultValue("VND").IsRequired();
        builder.Property(wallet => wallet.Status).HasDefaultValue(WalletStatus.Active);
        builder.HasIndex(wallet => wallet.UserId).IsUnique();
        builder.HasOne(wallet => wallet.User).WithOne().HasForeignKey<Wallet>(wallet => wallet.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions", table =>
        {
            table.HasCheckConstraint("CK_WalletTransactions_Amount", "\"Amount\" <> 0");
            table.HasCheckConstraint("CK_WalletTransactions_Balance", "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0");
            table.HasCheckConstraint("CK_WalletTransactions_Held", "\"HeldBefore\" >= 0 AND \"HeldAfter\" >= 0");
        });
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Amount).HasPrecision(18, 2);
        builder.Property(transaction => transaction.BalanceBefore).HasPrecision(18, 2);
        builder.Property(transaction => transaction.BalanceAfter).HasPrecision(18, 2);
        builder.Property(transaction => transaction.HeldBefore).HasPrecision(18, 2);
        builder.Property(transaction => transaction.HeldAfter).HasPrecision(18, 2);
        builder.Property(transaction => transaction.ReferenceType).HasMaxLength(50).IsRequired();
        builder.Property(transaction => transaction.ReferenceId).HasMaxLength(100).IsRequired();
        builder.Property(transaction => transaction.Description).HasMaxLength(500);
        builder.Property(transaction => transaction.IdempotencyKey).HasMaxLength(150).IsRequired();
        builder.Property(transaction => transaction.AdjustmentReason).HasMaxLength(500);
        builder.HasIndex(transaction => transaction.IdempotencyKey).IsUnique();
        builder.HasIndex(transaction => new { transaction.WalletId, transaction.CreatedAt });
        builder.HasIndex(transaction => new { transaction.ReferenceType, transaction.ReferenceId });
        builder.HasOne(transaction => transaction.Wallet).WithMany(wallet => wallet.Transactions).HasForeignKey(transaction => transaction.WalletId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table => table.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0"));
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Amount).HasPrecision(18, 2);
        builder.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
        builder.Property(payment => payment.Purpose).HasMaxLength(100).IsRequired();
        builder.Property(payment => payment.ReferenceType).HasMaxLength(50).IsRequired();
        builder.Property(payment => payment.ReferenceId).HasMaxLength(100).IsRequired();
        builder.Property(payment => payment.Provider).HasMaxLength(30).IsRequired();
        builder.Property(payment => payment.ProviderTransactionId).HasMaxLength(100);
        builder.Property(payment => payment.IdempotencyKey).HasMaxLength(150).IsRequired();
        builder.Property(payment => payment.CheckoutUrl).HasMaxLength(2048);
        builder.Property(payment => payment.QrCode).HasMaxLength(2048);
        builder.HasIndex(payment => payment.ProviderOrderCode).IsUnique();
        builder.HasIndex(payment => new { payment.UserId, payment.IdempotencyKey }).IsUnique();
        builder.HasIndex(payment => new { payment.Provider, payment.ProviderTransactionId }).IsUnique().HasFilter("\"ProviderTransactionId\" IS NOT NULL");
        builder.HasIndex(payment => new { payment.UserId, payment.CreatedAt });
        builder.HasOne(payment => payment.Wallet).WithMany(wallet => wallet.Payments).HasForeignKey(payment => payment.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(payment => payment.User).WithMany().HasForeignKey(payment => payment.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
