using Microsoft.EntityFrameworkCore;
using Viora.Application.Wallets;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Wallets;

public sealed class AdminWalletService(AppDbContext dbContext) : IAdminWalletService
{
    public async Task<AdminPage<AdminWalletItem>> GetWalletsAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var query = dbContext.Wallets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(item => item.User.DisplayName.Contains(value) || item.UserId.ToString().Contains(value) || item.Id.ToString().Contains(value));
        }
        var total = await query.CountAsync(cancellationToken);
        var data = await query.OrderByDescending(item => item.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new AdminWalletItem(item.Id, item.UserId, item.User.DisplayName, item.AvailableBalance, item.HeldBalance, item.Currency, item.Status, item.CreatedAt, item.UpdatedAt)).ToListAsync(cancellationToken);
        return Page(data, page, pageSize, total);
    }

    public async Task<AdminPage<AdminTransactionItem>> GetTransactionsAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var query = dbContext.WalletTransactions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(item => item.Id.ToString().Contains(value) || item.ReferenceId.Contains(value) || item.Wallet.User.DisplayName.Contains(value));
        }
        var total = await query.CountAsync(cancellationToken);
        var data = await query.OrderByDescending(item => item.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new AdminTransactionItem(item.Id, item.WalletId, item.Wallet.UserId, item.Wallet.User.DisplayName, item.Type, item.Amount, item.Status, item.ReferenceType, item.ReferenceId, item.Description, item.CreatedAt)).ToListAsync(cancellationToken);
        return Page(data, page, pageSize, total);
    }

    public async Task<AdminPage<AdminPaymentItem>> GetPaymentsAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var query = dbContext.Payments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(item => item.Id.ToString().Contains(value) || item.ProviderOrderCode.ToString().Contains(value) || (item.ProviderTransactionId != null && item.ProviderTransactionId.Contains(value)) || item.User.DisplayName.Contains(value));
        }
        var total = await query.CountAsync(cancellationToken);
        var data = await query.OrderByDescending(item => item.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new AdminPaymentItem(item.Id, item.UserId, item.User.DisplayName, item.Amount, item.Currency, item.Provider, item.ProviderOrderCode, item.ProviderTransactionId, item.Status, item.CreatedAt, item.PaidAt)).ToListAsync(cancellationToken);
        return Page(data, page, pageSize, total);
    }

    private static (int Page, int PageSize) Normalize(int page, int pageSize) => (Math.Max(page, 1), Math.Clamp(pageSize, 1, 100));
    private static AdminPage<T> Page<T>(IReadOnlyList<T> data, int page, int pageSize, int total) => new(data, page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
}
