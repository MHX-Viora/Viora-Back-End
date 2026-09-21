namespace Viora.Domain.Entities;

public sealed class Wallet : AuditableEntity
{
    public Guid UserId { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal HeldBalance { get; set; }
    public long AnktCoinBalance { get; set; }
    public string Currency { get; set; } = "VND";
    public WalletStatus Status { get; set; } = WalletStatus.Active;
    public User User { get; set; } = null!;
    public ICollection<WalletTransaction> Transactions { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<Withdrawal> Withdrawals { get; set; } = [];
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
    public Guid? LedgerTransactionId { get; set; }
    public long ProviderOrderCode { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Wallet Wallet { get; set; } = null!;
    public User User { get; set; } = null!;
    public WalletTransaction? LedgerTransaction { get; set; }
}

public sealed class BankAccount : AuditableEntity
{
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = null!;
    public string BankName { get; set; } = null!;
    public string AccountNumberEncrypted { get; set; } = null!;
    public string AccountNumberHash { get; set; } = null!;
    public string AccountNumberLast4 { get; set; } = null!;
    public string AccountHolderName { get; set; } = null!;
    public bool IsDefault { get; set; }
    public User User { get; set; } = null!;
    public ICollection<Withdrawal> Withdrawals { get; set; } = [];
}

public sealed class Withdrawal : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid WalletId { get; set; }
    public Guid BankAccountId { get; set; }
    public Guid LedgerTransactionId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal NetAmount { get; set; }
    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Pending;
    public string TransactionCode { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string BankCode { get; set; } = null!;
    public string BankName { get; set; } = null!;
    public string BankAccountLast4 { get; set; } = null!;
    public string BankAccountHolderName { get; set; } = null!;
    public string? FailureReason { get; set; }
    public DateTime? ProcessingAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Wallet Wallet { get; set; } = null!;
    public User User { get; set; } = null!;
    public BankAccount BankAccount { get; set; } = null!;
    public WalletTransaction LedgerTransaction { get; set; } = null!;
}
