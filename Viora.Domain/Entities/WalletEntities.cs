namespace Viora.Domain.Entities;

public sealed class Wallet : AuditableEntity
{
    public Guid UserId { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal HeldBalance { get; set; }
    public string Currency { get; set; } = "VND";
    public WalletStatus Status { get; set; } = WalletStatus.Active;
    public User User { get; set; } = null!;
    public ICollection<WalletTransaction> Transactions { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public sealed class WalletTransaction : CreatedEntity
{
    public Guid WalletId { get; set; }
    public WalletTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal HeldBefore { get; set; }
    public decimal HeldAfter { get; set; }
    public string ReferenceType { get; set; } = null!;
    public string ReferenceId { get; set; } = null!;
    public string? Description { get; set; }
    public WalletTransactionStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public DateTime? CompletedAt { get; set; }
    public Guid? AdminId { get; set; }
    public string? AdjustmentReason { get; set; }
    public Wallet Wallet { get; set; } = null!;
}

public sealed class Payment : CreatedEntity
{
    public Guid UserId { get; set; }
    public Guid WalletId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Purpose { get; set; } = null!;
    public string ReferenceType { get; set; } = null!;
    public string ReferenceId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string? ProviderTransactionId { get; set; }
    public long ProviderOrderCode { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Wallet Wallet { get; set; } = null!;
    public User User { get; set; } = null!;
}
