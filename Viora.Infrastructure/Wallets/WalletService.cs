using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Wallets;

public sealed class WalletService(AppDbContext dbContext) : IWalletService
{
    public async Task<WalletResponse> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken) =>
        Map(await GetOrCreateEntityAsync(userId, cancellationToken));

    public async Task<WalletTransactionPage> GetTransactionsAsync(
        Guid userId, int page, int pageSize, WalletTransactionType? type, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var walletId = await dbContext.Wallets.Where(wallet => wallet.UserId == userId)
            .Select(wallet => (Guid?)wallet.Id).SingleOrDefaultAsync(cancellationToken);
        if (walletId is null) return new WalletTransactionPage([], page, pageSize, 0, 0);

        var query = dbContext.WalletTransactions.AsNoTracking().Where(item => item.WalletId == walletId);
        if (type is not null) query = query.Where(item => item.Type == type);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new WalletTransactionPage(items.Select(Map).ToArray(), page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<WalletTransactionResponse?> GetTransactionAsync(
        Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var item = await dbContext.WalletTransactions.AsNoTracking()
            .SingleOrDefaultAsync(transaction => transaction.Id == transactionId && transaction.Wallet.UserId == userId, cancellationToken);
        return item is null ? null : Map(item);
    }

    public async Task<WalletTransactionResponse> CompleteDepositAsync(
        long providerOrderCode, string providerTransactionId, decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        RequireIdempotencyKey(idempotencyKey);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var existing = await FindIdempotentAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(existing);
        }

        var payment = await dbContext.Payments
            .SingleOrDefaultAsync(item => item.ProviderOrderCode == providerOrderCode, cancellationToken)
            ?? throw new WalletNotFoundException();
        if (payment.Amount != amount)
            throw new WalletConflictException("PAYMENT_AMOUNT_MISMATCH", "Số tiền webhook không khớp payment.");
        if (payment.Status == PaymentStatus.Paid)
        {
            var completedDeposit = await dbContext.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(
                item => item.Type == WalletTransactionType.Deposit &&
                        item.ReferenceType == "Payment" &&
                        item.ReferenceId == payment.Id.ToString("D"), cancellationToken);
            if (completedDeposit is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Map(completedDeposit);
            }
            throw new WalletConflictException("PAYMENT_LEDGER_MISSING", "Payment đã hoàn tất nhưng không tìm thấy bút toán.");
        }
        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Expired)
            throw new WalletConflictException("PAYMENT_NOT_PAYABLE", "Payment không còn ở trạng thái có thể thanh toán.");

        var wallet = await dbContext.Wallets
            .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {payment.WalletId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        existing = await FindIdempotentAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(existing);
        }
        EnsureActive(wallet);
        var completedAt = DateTime.UtcNow;
        var ledger = NewTransaction(wallet, WalletTransactionType.Deposit, amount, "Payment", payment.Id.ToString("D"), "Nạp tiền qua payOS", idempotencyKey, completedAt);
        wallet.AvailableBalance += amount;
        ledger.BalanceAfter = wallet.AvailableBalance;
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt ??= completedAt;
        payment.ProviderTransactionId ??= providerTransactionId;
        dbContext.WalletTransactions.Add(ledger);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(ledger);
    }

    public Task<WalletTransactionResponse> HoldAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken) =>
        MutateAsync(userId, amount, WalletTransactionType.Hold, referenceType, referenceId, idempotencyKey, (wallet, value) =>
        {
            WalletFinancialRules.RequireSufficientBalance(wallet.AvailableBalance, value);
            wallet.AvailableBalance -= value;
            wallet.HeldBalance += value;
            return -value;
        }, cancellationToken);

    public Task<WalletTransactionResponse> ReleaseAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken) =>
        MutateAsync(userId, amount, WalletTransactionType.Release, referenceType, referenceId, idempotencyKey, (wallet, value) =>
        {
            RequireHeld(wallet, value);
            wallet.HeldBalance -= value;
            wallet.AvailableBalance += value;
            return value;
        }, cancellationToken);

    public Task<WalletTransactionResponse> CaptureAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken) =>
        MutateAsync(userId, amount, WalletTransactionType.Capture, referenceType, referenceId, idempotencyKey, (wallet, value) =>
        {
            RequireHeld(wallet, value);
            wallet.HeldBalance -= value;
            return -value;
        }, cancellationToken);

    public Task<WalletTransactionResponse> RefundAsync(Guid userId, decimal amount, string referenceType, string referenceId, string idempotencyKey, CancellationToken cancellationToken) =>
        MutateAsync(userId, amount, WalletTransactionType.Refund, referenceType, referenceId, idempotencyKey, (wallet, value) =>
        {
            wallet.AvailableBalance += value;
            return value;
        }, cancellationToken);

    public async Task<WalletTransactionResponse> AdjustAsync(Guid userId, Guid adminId, decimal amount, string reason, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (amount == 0) throw new WalletValidationException("INVALID_AMOUNT", "Số tiền điều chỉnh phải khác 0.");
        if (string.IsNullOrWhiteSpace(reason)) throw new WalletValidationException("ADJUSTMENT_REASON_REQUIRED", "Lý do điều chỉnh là bắt buộc.");
        if (reason.Trim().Length > 500) throw new WalletValidationException("ADJUSTMENT_REASON_TOO_LONG", "Lý do điều chỉnh không được vượt quá 500 ký tự.");
        if (!await dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken))
            throw new WalletValidationException("WALLET_USER_NOT_FOUND", "Không tìm thấy người dùng cần điều chỉnh ví.");
        return await MutateAsync(userId, Math.Abs(amount), WalletTransactionType.Adjustment, "AdminAdjustment", adminId.ToString("D"), idempotencyKey, (wallet, _) =>
        {
            if (amount < 0) WalletFinancialRules.RequireSufficientBalance(wallet.AvailableBalance, -amount);
            wallet.AvailableBalance += amount;
            return amount;
        }, cancellationToken, adminId, reason.Trim());
    }

    private async Task<WalletTransactionResponse> MutateAsync(
        Guid userId, decimal amount, WalletTransactionType type, string referenceType, string referenceId,
        string idempotencyKey, Func<Wallet, decimal, decimal> mutation, CancellationToken cancellationToken,
        Guid? adminId = null, string? adjustmentReason = null)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        RequireIdempotencyKey(idempotencyKey);
        var walletId = (await GetOrCreateEntityAsync(userId, cancellationToken)).Id;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await FindIdempotentAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(existing);
        }
        var wallet = await dbContext.Wallets
            .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {walletId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        existing = await FindIdempotentAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(existing);
        }
        EnsureActive(wallet);
        var beforeAvailable = wallet.AvailableBalance;
        var beforeHeld = wallet.HeldBalance;
        var signedAmount = mutation(wallet, amount);
        var completedAt = DateTime.UtcNow;
        var ledger = new WalletTransaction
        {
            WalletId = wallet.Id, Type = type, Amount = signedAmount,
            BalanceBefore = beforeAvailable, BalanceAfter = wallet.AvailableBalance,
            HeldBefore = beforeHeld, HeldAfter = wallet.HeldBalance,
            ReferenceType = referenceType, ReferenceId = referenceId,
            Status = WalletTransactionStatus.Completed, IdempotencyKey = idempotencyKey,
            CompletedAt = completedAt, AdminId = adminId, AdjustmentReason = adjustmentReason
        };
        dbContext.WalletTransactions.Add(ledger);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(ledger);
    }

    private async Task<Wallet> GetOrCreateEntityAsync(Guid userId, CancellationToken cancellationToken)
    {
        var wallet = await dbContext.Wallets.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (wallet is not null) return wallet;
        wallet = new Wallet { UserId = userId, Currency = "VND", Status = WalletStatus.Active };
        dbContext.Wallets.Add(wallet);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return wallet;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(wallet).State = EntityState.Detached;
            return await dbContext.Wallets.SingleAsync(item => item.UserId == userId, cancellationToken);
        }
    }

    private Task<WalletTransaction?> FindIdempotentAsync(string key, CancellationToken cancellationToken) =>
        dbContext.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(item => item.IdempotencyKey == key, cancellationToken);

    private static WalletTransaction NewTransaction(Wallet wallet, WalletTransactionType type, decimal amount, string referenceType, string referenceId, string? description, string key, DateTime completedAt) => new()
    {
        WalletId = wallet.Id, Type = type, Amount = amount,
        BalanceBefore = wallet.AvailableBalance, BalanceAfter = wallet.AvailableBalance,
        HeldBefore = wallet.HeldBalance, HeldAfter = wallet.HeldBalance,
        ReferenceType = referenceType, ReferenceId = referenceId, Description = description,
        Status = WalletTransactionStatus.Completed, IdempotencyKey = key, CompletedAt = completedAt
    };

    private static void EnsureActive(Wallet wallet)
    {
        if (wallet.Status != WalletStatus.Active) throw new WalletConflictException("WALLET_NOT_ACTIVE", "Ví hiện không hoạt động.");
    }

    private static void RequireHeld(Wallet wallet, decimal amount)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        if (wallet.HeldBalance < amount) throw new WalletValidationException("INSUFFICIENT_HELD_BALANCE", "Số tiền tạm giữ không đủ.");
    }

    private static void RequireIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 140) throw new WalletValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency key không hợp lệ.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static WalletResponse Map(Wallet wallet) => new(wallet.Id, wallet.AvailableBalance, wallet.HeldBalance, wallet.Currency, wallet.Status);
    private static WalletTransactionResponse Map(WalletTransaction item) => new(item.Id, item.Type, item.Amount, item.BalanceBefore, item.BalanceAfter, item.HeldBefore, item.HeldAfter, item.ReferenceType, item.ReferenceId, item.Description, item.Status, item.CreatedAt, item.CompletedAt);
}
