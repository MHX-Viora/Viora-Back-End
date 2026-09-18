using Viora.Domain.Entities;

namespace Viora.Application.Wallets;

public sealed record AdminWalletItem(Guid Id, Guid UserId, string DisplayName, decimal AvailableBalance, decimal HeldBalance, string Currency, WalletStatus Status, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record AdminTransactionItem(Guid Id, Guid WalletId, Guid UserId, string DisplayName, WalletTransactionType Type, decimal Amount, WalletTransactionStatus Status, string ReferenceType, string ReferenceId, string? Description, DateTime CreatedAt);
public sealed record AdminPaymentItem(Guid Id, Guid UserId, string DisplayName, decimal Amount, string Currency, string Provider, long ProviderOrderCode, string? ProviderTransactionId, PaymentStatus Status, DateTime CreatedAt, DateTime? PaidAt);
public sealed record AdminPage<T>(IReadOnlyList<T> Data, int Page, int PageSize, int TotalItems, int TotalPages);

public interface IAdminWalletService
{
    Task<AdminPage<AdminWalletItem>> GetWalletsAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken);
    Task<AdminPage<AdminTransactionItem>> GetTransactionsAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken);
    Task<AdminPage<AdminPaymentItem>> GetPaymentsAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken);
}
