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
        Guid userId, int page, int pageSize, WalletTransactionType? type, WalletHistoryGroup? group, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var walletId = await dbContext.Wallets.Where(wallet => wallet.UserId == userId)
            .Select(wallet => (Guid?)wallet.Id).SingleOrDefaultAsync(cancellationToken);
        if (walletId is null) return new WalletTransactionPage([], page, pageSize, 0, 0);
        await ExpirePaymentsAsync(walletId.Value, cancellationToken);

        var ledgerQuery = dbContext.WalletTransactions.AsNoTracking()
            .Where(item => item.WalletId == walletId &&
                (item.Type != WalletTransactionType.Deposit || item.ReferenceType != "Payment" ||
                 !dbContext.Payments.Any(payment => payment.WalletId == walletId &&
                     (payment.LedgerTransactionId == item.Id || payment.Id.ToString().ToLower() == item.ReferenceId.ToLower()))));
        if (type is not null) ledgerQuery = ledgerQuery.Where(item => item.Type == type);
        if (group is not null) ledgerQuery = group switch
        {
            WalletHistoryGroup.Deposit => ledgerQuery.Where(item => item.Type == WalletTransactionType.Deposit),
            WalletHistoryGroup.Withdrawal => ledgerQuery.Where(item => item.Type == WalletTransactionType.Withdrawal),
            WalletHistoryGroup.Payment => ledgerQuery.Where(item => item.Type == WalletTransactionType.Payment || item.Type == WalletTransactionType.TransferOut || item.Type == WalletTransactionType.Capture),
            WalletHistoryGroup.Refund => ledgerQuery.Where(item => item.Type == WalletTransactionType.Refund || item.Type == WalletTransactionType.Release),
            _ => ledgerQuery
        };

        var paymentQuery = dbContext.Payments.AsNoTracking()
            .Where(item => item.WalletId == walletId && item.Purpose == "WalletDeposit");
        if (type is not null and not WalletTransactionType.Deposit || group is not null and not WalletHistoryGroup.Deposit)
            paymentQuery = paymentQuery.Where(_ => false);

        var history = ledgerQuery.Select(item => new { item.Id, item.CreatedAt, IsPayment = false })
            .Concat(paymentQuery.Select(item => new { item.Id, item.CreatedAt, IsPayment = true }));
        var total = await history.CountAsync(cancellationToken);
        var entries = await history.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var transactionIds = entries.Where(item => !item.IsPayment).Select(item => item.Id).ToArray();
        var paymentIds = entries.Where(item => item.IsPayment).Select(item => item.Id).ToArray();
        var payments = await dbContext.Payments.AsNoTracking().Where(item => paymentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var linkedIds = payments.Values.Where(item => item.LedgerTransactionId.HasValue)
            .Select(item => item.LedgerTransactionId!.Value).ToArray();
        var paymentReferences = paymentIds.Select(item => item.ToString("D")).ToArray();
        var ledgers = await dbContext.WalletTransactions.AsNoTracking().Where(item =>
                transactionIds.Contains(item.Id) || linkedIds.Contains(item.Id) ||
                (item.Type == WalletTransactionType.Deposit && item.ReferenceType == "Payment" && paymentReferences.Contains(item.ReferenceId)))
            .ToListAsync(cancellationToken);
        var ledgerById = ledgers.ToDictionary(item => item.Id);
        var ledgerByReference = ledgers.Where(item => item.Type == WalletTransactionType.Deposit && item.ReferenceType == "Payment")
            .GroupBy(item => item.ReferenceId).ToDictionary(grouped => grouped.Key, grouped => grouped.First());
        var withdrawals = await dbContext.Withdrawals.AsNoTracking()
            .Where(item => transactionIds.Contains(item.LedgerTransactionId))
            .ToDictionaryAsync(item => item.LedgerTransactionId, cancellationToken);
        var advertisementIds = ledgers.Where(item => item.ReferenceType is "Advertisement" or "AdvertisementEvent")
            .Select(item => Guid.TryParse(item.ReferenceId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var advertisementDetails = await dbContext.Advertisements.AsNoTracking()
            .Where(item => advertisementIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Post.Content, item.ReservedAmount, item.SpentAmount })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var data = entries.Select(entry =>
        {
            if (!entry.IsPayment)
            {
                var ledger = ledgerById[entry.Id];
                withdrawals.TryGetValue(ledger.Id, out var withdrawal);
                var advertisement = Guid.TryParse(ledger.ReferenceId, out var advertisementId)
                    ? advertisementDetails.GetValueOrDefault(advertisementId)
                    : null;
                var relatedStatus = ledger.Type == WalletTransactionType.Hold && advertisement is not null
                    ? advertisement.ReservedAmount > 0 ? "Đang tạm giữ" : advertisement.SpentAmount > 0 ? "Đã quyết toán" : "Đã giải phóng"
                    : null;
                return Map(ledger, withdrawal?.Status, null, withdrawal, advertisement?.Content, relatedStatus);
            }
            var payment = payments[entry.Id];
            var linked = payment.LedgerTransactionId is Guid linkedId && ledgerById.TryGetValue(linkedId, out var byId)
                ? byId
                : ledgerByReference.GetValueOrDefault(payment.Id.ToString("D"));
            return MapDepositPayment(payment, linked);
        }).ToArray();
        return new WalletTransactionPage(data, page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<WalletTransactionResponse?> GetTransactionAsync(
        Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == transactionId && item.UserId == userId && item.Purpose == "WalletDeposit", cancellationToken);
        if (payment is not null)
        {
            if (PaymentLifecycle.ShouldExpire(payment.Status, payment.ExpiresAt, DateTime.UtcNow))
            {
                await ExpirePaymentsAsync(payment.WalletId, cancellationToken);
                payment = await dbContext.Payments.AsNoTracking().SingleAsync(item => item.Id == transactionId, cancellationToken);
            }
            var reference = payment.Id.ToString("D");
            var linkedLedger = await dbContext.WalletTransactions.AsNoTracking().FirstOrDefaultAsync(item =>
                item.WalletId == payment.WalletId &&
                (item.Id == payment.LedgerTransactionId ||
                 (item.Type == WalletTransactionType.Deposit && item.ReferenceType == "Payment" && item.ReferenceId == reference)),
                cancellationToken);
            return MapDepositPayment(payment, linkedLedger);
        }
        var item = await dbContext.WalletTransactions.AsNoTracking()
            .SingleOrDefaultAsync(transaction => transaction.Id == transactionId && transaction.Wallet.UserId == userId, cancellationToken);
        if (item is null) return null;
        var withdrawal = item.Type == WalletTransactionType.Withdrawal
            ? await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(withdrawal => withdrawal.LedgerTransactionId == item.Id, cancellationToken)
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
        var advertisement = item.ReferenceType is "Advertisement" or "AdvertisementEvent" &&
                             Guid.TryParse(item.ReferenceId, out var advertisementId)
            ? await dbContext.Advertisements.AsNoTracking().Where(advertisement => advertisement.Id == advertisementId)
                .Select(advertisement => new { advertisement.Post.Content, advertisement.ReservedAmount, advertisement.SpentAmount })
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var relatedStatus = item.Type == WalletTransactionType.Hold && advertisement is not null
            ? advertisement.ReservedAmount > 0 ? "Đang tạm giữ" : advertisement.SpentAmount > 0 ? "Đã quyết toán" : "Đã giải phóng"
            : null;
        return Map(item, withdrawal?.Status, paymentStatus, withdrawal, advertisement?.Content, relatedStatus);
    }

    public async Task<WalletTransactionResponse> CompleteDepositAsync(
        long providerOrderCode, string providerTransactionId, decimal amount, CancellationToken cancellationToken)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var paymentQuery = dbContext.Database.IsNpgsql()
            ? dbContext.Payments.FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"ProviderOrderCode\" = {providerOrderCode} FOR UPDATE")
            : dbContext.Payments.Where(item => item.ProviderOrderCode == providerOrderCode);
        var payment = await paymentQuery.SingleOrDefaultAsync(cancellationToken)
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
                if (ledger.BalanceAfter - ledger.BalanceBefore != payment.Amount)
                    throw new WalletConflictException("PAYMENT_LEDGER_INCONSISTENT", "Payment đã hoàn tất nhưng bút toán chưa ghi nhận thay đổi số dư.");
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

        var walletQuery = dbContext.Database.IsNpgsql()
            ? dbContext.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {payment.WalletId} FOR UPDATE")
            : dbContext.Wallets.Where(item => item.Id == payment.WalletId);
        var wallet = await walletQuery.SingleAsync(cancellationToken);
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
            var walletQuery = dbContext.Database.IsNpgsql()
                ? dbContext.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {walletId} FOR UPDATE")
                : dbContext.Wallets.Where(item => item.Id == walletId);
            var wallet = await walletQuery.SingleAsync(cancellationToken);
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
        var expiredQuery = dbContext.Database.IsNpgsql()
            ? dbContext.Payments.FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"WalletId\" = {walletId} AND \"Status\" = 0 AND \"ExpiresAt\" <= {now} FOR UPDATE")
            : dbContext.Payments.Where(item => item.WalletId == walletId && item.Status == PaymentStatus.Pending && item.ExpiresAt <= now);
        var expired = await expiredQuery.ToListAsync(cancellationToken);
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
    private static WalletTransactionResponse MapDepositPayment(Payment payment, WalletTransaction? ledger) => new(
        payment.Id,
        WalletTransactionType.Deposit,
        payment.Amount,
        ledger?.BalanceBefore ?? 0,
        ledger?.BalanceAfter ?? 0,
        ledger?.HeldBefore ?? 0,
        ledger?.HeldAfter ?? 0,
        "Payment",
        payment.Id.ToString("D"),
        ledger?.Description ?? "Nạp tiền bằng mã QR",
        payment.Status == PaymentStatus.Paid ? WalletTransactionStatus.Completed :
            payment.Status == PaymentStatus.Pending ? WalletTransactionStatus.Pending : WalletTransactionStatus.Failed,
        null,
        payment.Status,
        payment.CreatedAt,
        payment.PaidAt ?? payment.CancelledAt ?? payment.FailedAt ??
            (payment.Status == PaymentStatus.Expired ? payment.ExpiresAt : null),
        ledger is not null,
        "Ngân hàng",
        "Ví ANKT");
    private static WalletTransactionResponse Map(
        WalletTransaction item, WithdrawalStatus? withdrawalStatus = null, PaymentStatus? paymentStatus = null,
        Withdrawal? withdrawal = null, string? relatedContent = null, string? relatedStatus = null)
    {
        var (source, destination) = item.Type switch
        {
            WalletTransactionType.Deposit => ("Ngân hàng", "Ví ANKT"),
            WalletTransactionType.Withdrawal => ("Ví ANKT", withdrawal is null
                ? "Tài khoản ngân hàng" : $"{withdrawal.BankName} •••• {withdrawal.BankAccountLast4}"),
            WalletTransactionType.Hold => ("Ví ANKT", "Số dư tạm giữ"),
            WalletTransactionType.Release => ("Số dư tạm giữ", "Ví ANKT"),
            WalletTransactionType.Capture => ("Số dư tạm giữ", "Chi phí quảng cáo"),
            WalletTransactionType.Refund => ("Khoản hoàn tiền", "Ví ANKT"),
            WalletTransactionType.Payment or WalletTransactionType.TransferOut => ("Ví ANKT", "Thanh toán"),
            WalletTransactionType.TransferIn => ("Người gửi", "Ví ANKT"),
            WalletTransactionType.Adjustment when item.Amount >= 0 => ("Điều chỉnh", "Ví ANKT"),
            WalletTransactionType.Adjustment => ("Ví ANKT", "Điều chỉnh"),
            _ => ("Ví ANKT", "Giao dịch")
        };
        var description = item.Description ?? (item.Type, item.ReferenceType) switch
        {
            (WalletTransactionType.Hold, "Advertisement") => "Ngân sách quảng cáo được chuyển sang số dư tạm giữ.",
            (WalletTransactionType.Release, "Advertisement") => "Ngân sách quảng cáo chưa sử dụng được hoàn về ví.",
            (WalletTransactionType.Capture, "AdvertisementEvent") => "Chi phí phát sinh khi quảng cáo được phân phối.",
            _ => null
        };
        return new WalletTransactionResponse(item.Id, item.Type, item.Amount, item.BalanceBefore, item.BalanceAfter,
            item.HeldBefore, item.HeldAfter, item.ReferenceType, item.ReferenceId, description, item.Status,
            withdrawalStatus, paymentStatus, item.CreatedAt, item.CompletedAt, true, source, destination, relatedContent, relatedStatus);
    }
}
