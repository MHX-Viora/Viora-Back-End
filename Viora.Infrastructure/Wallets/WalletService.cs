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
        await ExpirePaymentsAsync(walletId.Value, cancellationToken);

        var query = dbContext.WalletTransactions.AsNoTracking().Where(item => item.WalletId == walletId);
        if (type is not null) query = query.Where(item => item.Type == type);
        else query = query.Where(item => item.Type != WalletTransactionType.Hold && item.Type != WalletTransactionType.Release && item.Type != WalletTransactionType.Capture);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var transactionIds = items.Select(item => item.Id).ToArray();
        var withdrawalStatuses = await dbContext.Withdrawals.AsNoTracking()
            .Where(item => transactionIds.Contains(item.LedgerTransactionId))
            .ToDictionaryAsync(item => item.LedgerTransactionId, item => item.Status, cancellationToken);
        var depositPaymentIds = items.Where(item => item.Type == WalletTransactionType.Deposit && item.ReferenceType == "Payment")
            .Select(item => Guid.TryParse(item.ReferenceId, out var paymentId) ? paymentId : Guid.Empty)
            .Where(paymentId => paymentId != Guid.Empty).ToArray();
        var payments = await dbContext.Payments.AsNoTracking()
            .Where(payment => (payment.LedgerTransactionId.HasValue && transactionIds.Contains(payment.LedgerTransactionId.Value)) || depositPaymentIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.LedgerTransactionId, payment.Status })
            .ToListAsync(cancellationToken);
        var paymentByLedger = payments.Where(payment => payment.LedgerTransactionId.HasValue)
            .ToDictionary(payment => payment.LedgerTransactionId!.Value, payment => payment.Status);
        var paymentById = payments.ToDictionary(payment => payment.Id, payment => payment.Status);
        PaymentStatus? ResolvePaymentStatus(WalletTransaction item)
        {
            if (paymentByLedger.TryGetValue(item.Id, out var linked)) return linked;
            return Guid.TryParse(item.ReferenceId, out var paymentId) && paymentById.TryGetValue(paymentId, out var legacy)
                ? legacy
                : null;
        }
        return new WalletTransactionPage(items.Select(item => Map(item, withdrawalStatuses.GetValueOrDefault(item.Id), ResolvePaymentStatus(item))).ToArray(), page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<WalletTransactionResponse?> GetTransactionAsync(
        Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var item = await dbContext.WalletTransactions.AsNoTracking()
            .SingleOrDefaultAsync(transaction => transaction.Id == transactionId && transaction.Wallet.UserId == userId, cancellationToken);
        if (item is null) return null;
        var withdrawalStatus = item.Type == WalletTransactionType.Withdrawal
            ? await dbContext.Withdrawals.AsNoTracking().Where(withdrawal => withdrawal.LedgerTransactionId == item.Id).Select(withdrawal => (WithdrawalStatus?)withdrawal.Status).SingleOrDefaultAsync(cancellationToken)
            : null;
        PaymentStatus? paymentStatus = null;
        if (item.Type == WalletTransactionType.Deposit && item.ReferenceType == "Payment")
        {
            var paymentId = Guid.TryParse(item.ReferenceId, out var parsed) ? parsed : Guid.Empty;
            paymentStatus = await dbContext.Payments.AsNoTracking()
                .Where(payment => payment.LedgerTransactionId == item.Id || payment.Id == paymentId)
                .Select(payment => (PaymentStatus?)payment.Status)
                .SingleOrDefaultAsync(cancellationToken);
        }
        return Map(item, withdrawalStatus, paymentStatus);
    }

    public async Task<WalletTransactionResponse> CompleteDepositAsync(
        long providerOrderCode, string providerTransactionId, decimal amount, CancellationToken cancellationToken)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var payment = await dbContext.Payments
            .FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"ProviderOrderCode\" = {providerOrderCode} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WalletNotFoundException();
        if (payment.Amount != amount)
            throw new WalletConflictException("PAYMENT_AMOUNT_MISMATCH", "Số tiền webhook không khớp payment.");
        var ledger = payment.LedgerTransactionId is Guid ledgerId
            ? await dbContext.WalletTransactions.SingleOrDefaultAsync(item => item.Id == ledgerId, cancellationToken)
            : await dbContext.WalletTransactions.SingleOrDefaultAsync(
                item => item.Type == WalletTransactionType.Deposit && item.ReferenceType == "Payment" && item.ReferenceId == payment.Id.ToString("D"),
                cancellationToken);
        if (payment.Status == PaymentStatus.Paid)
        {
            if (ledger is null) throw new WalletConflictException("PAYMENT_LEDGER_MISSING", "Payment đã hoàn tất nhưng không tìm thấy bút toán.");
            if (ledger.Status == WalletTransactionStatus.Pending)
            {
                ledger.Status = WalletTransactionStatus.Completed;
                ledger.CompletedAt ??= payment.PaidAt ?? DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else await transaction.RollbackAsync(cancellationToken);
            return Map(ledger, paymentStatus: PaymentStatus.Paid);
        }
        if (!PaymentLifecycle.CanComplete(payment.Status))
            throw new WalletConflictException("PAYMENT_NOT_PAYABLE", "Payment không còn ở trạng thái có thể thanh toán.");

        var wallet = await dbContext.Wallets
            .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {payment.WalletId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        EnsureActive(wallet);
        var completedAt = DateTime.UtcNow;
        if (ledger?.Status == WalletTransactionStatus.Completed)
        {
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt ??= ledger.CompletedAt ?? completedAt;
            payment.ProviderTransactionId ??= providerTransactionId;
            payment.LedgerTransactionId ??= ledger.Id;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(ledger, paymentStatus: PaymentStatus.Paid);
        }

        ledger ??= NewTransaction(wallet, WalletTransactionType.Deposit, amount, "Payment", payment.Id.ToString("D"), "Nạp tiền bằng mã QR", PaymentLifecycle.DepositIdempotencyKey(payment.Id), completedAt);
        ledger.BalanceBefore = wallet.AvailableBalance;
        wallet.AvailableBalance += amount;
        ledger.BalanceAfter = wallet.AvailableBalance;
        ledger.HeldBefore = wallet.HeldBalance;
        ledger.HeldAfter = wallet.HeldBalance;
        ledger.Status = WalletTransactionStatus.Completed;
        ledger.CompletedAt = completedAt;
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt ??= completedAt;
        payment.ProviderTransactionId ??= providerTransactionId;
        payment.LedgerTransactionId ??= ledger.Id;
        if (dbContext.Entry(ledger).State == EntityState.Detached) dbContext.WalletTransactions.Add(ledger);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(ledger, paymentStatus: PaymentStatus.Paid);
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
        var ownedTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var existing = await FindIdempotentAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
                return Map(existing);
            }
            var wallet = await dbContext.Wallets
                .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {walletId} FOR UPDATE")
                .SingleAsync(cancellationToken);
            existing = await FindIdempotentAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
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
            if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
            return Map(ledger);
        }
        finally
        {
            if (ownedTransaction is not null) await ownedTransaction.DisposeAsync();
        }
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

    private async Task ExpirePaymentsAsync(Guid walletId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var expired = await dbContext.Payments
            .FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"WalletId\" = {walletId} AND \"Status\" = 0 AND \"ExpiresAt\" <= {now} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        foreach (var payment in expired) payment.Status = PaymentStatus.Expired;
        var ledgerIds = expired.Where(payment => payment.LedgerTransactionId.HasValue)
            .Select(payment => payment.LedgerTransactionId!.Value).ToArray();
        var ledgers = await dbContext.WalletTransactions
            .Where(item => ledgerIds.Contains(item.Id) && item.Status == WalletTransactionStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var ledger in ledgers) ledger.Status = WalletTransactionStatus.Failed;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
    private static WalletResponse Map(Wallet wallet) => new(wallet.Id, wallet.AvailableBalance, wallet.HeldBalance, wallet.AnktCoinBalance, wallet.Currency, wallet.Status);
    private static WalletTransactionResponse Map(WalletTransaction item, WithdrawalStatus? withdrawalStatus = null, PaymentStatus? paymentStatus = null) => new(item.Id, item.Type, item.Amount, item.BalanceBefore, item.BalanceAfter, item.HeldBefore, item.HeldAfter, item.ReferenceType, item.ReferenceId, item.Description, item.Status, withdrawalStatus, paymentStatus, item.CreatedAt, item.CompletedAt);
}
