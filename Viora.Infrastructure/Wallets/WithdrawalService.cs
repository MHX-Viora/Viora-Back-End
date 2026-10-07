using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Wallets;

public sealed class WithdrawalService(
    AppDbContext dbContext,
    IWalletService walletService,
    IBankAccountProtector protector,
    IOptions<WithdrawalOptions> options) : IWithdrawalService
{
    private readonly WithdrawalOptions settings = options.Value;

    public async Task<IReadOnlyList<BankAccountResponse>> GetBankAccountsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await dbContext.BankAccounts.AsNoTracking().Where(item => item.UserId == userId)
            .OrderByDescending(item => item.IsDefault).ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<BankAccountResponse> CreateBankAccountAsync(Guid userId, CreateBankAccountRequest request, CancellationToken cancellationToken)
    {
        var number = NormalizeAccountNumber(request.AccountNumber);
        var bank = BankCatalogue.Find(request.BankCode) ?? throw new WalletValidationException("INVALID_BANK_CODE", "Hãy chọn ngân hàng trong danh sách.");
        var bankCode = bank.Code;
        var bankName = bank.Name;
        var holder = Required(request.AccountHolderName, 120, "INVALID_ACCOUNT_HOLDER").ToUpperInvariant();
        var walletId = (await walletService.GetOrCreateAsync(userId, cancellationToken)).Id;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(LockIsolation, cancellationToken);
        await LockWallet(walletId).SingleAsync(cancellationToken);
        var hash = protector.Hash($"{bankCode}:{number}");
        var legacyHash = protector.Hash(number);
        if (await dbContext.BankAccounts.AnyAsync(item => item.UserId == userId && (item.AccountNumberHash == hash || (item.BankCode == bankCode && item.AccountNumberHash == legacyHash)), cancellationToken))
            throw new WalletConflictException("BANK_ACCOUNT_EXISTS", "Tài khoản ngân hàng đã tồn tại.");

        var makeDefault = request.IsDefault || !await dbContext.BankAccounts.AnyAsync(item => item.UserId == userId, cancellationToken);
        if (makeDefault)
        {
            var defaults = await dbContext.BankAccounts.Where(item => item.UserId == userId && item.IsDefault).ToListAsync(cancellationToken);
            foreach (var item in defaults) item.IsDefault = false;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var account = new BankAccount
        {
            UserId = userId, BankCode = bankCode, BankName = bankName,
            AccountNumberEncrypted = protector.Protect(number), AccountNumberHash = hash,
            AccountNumberLast4 = number[^4..], AccountHolderName = holder, IsDefault = makeDefault
        };
        dbContext.BankAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(account);
    }

    public async Task<WithdrawalFeeSettingsResponse> GetFeeSettingsAsync(CancellationToken cancellationToken)
    {
        var current = await dbContext.WithdrawalFeeSettings.AsNoTracking().SingleAsync(cancellationToken);
        return new(current.FeePercent, current.Version, current.UpdatedAt, current.UpdatedBy);
    }

    public async Task<WithdrawalFeeSettingsResponse> UpdateFeeSettingsAsync(Guid adminId, decimal feePercent, long expectedVersion, CancellationToken cancellationToken)
    {
        RequireFeePercent(feePercent);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(LockIsolation, cancellationToken);
        var query = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL"
            ? dbContext.WithdrawalFeeSettings.FromSqlInterpolated($"SELECT * FROM \"WithdrawalFeeSettings\" WHERE \"Id\" = 1 FOR UPDATE")
            : dbContext.WithdrawalFeeSettings.AsQueryable();
        var current = await query.SingleAsync(cancellationToken);
        await dbContext.Entry(current).ReloadAsync(cancellationToken);
        if (current.Version != expectedVersion)
            throw new WalletConflictException("WITHDRAWAL_FEE_SETTINGS_CHANGED", "Cấu hình đã được người khác cập nhật. Hãy tải lại trước khi lưu.");
        if (current.FeePercent == feePercent)
            return new(current.FeePercent, current.Version, current.UpdatedAt, current.UpdatedBy);
        var previous = current.FeePercent;
        current.FeePercent = feePercent;
        current.Version++;
        current.UpdatedAt = DateTime.UtcNow;
        current.UpdatedBy = adminId;
        dbContext.AdminLogs.Add(new AdminLog
        {
            AdminId = adminId, Action = "UpdateWithdrawalFee", TargetType = "WithdrawalFeeSettings",
            Description = JsonSerializer.Serialize(new { previousFeePercent = previous, feePercent, version = current.Version })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(current.FeePercent, current.Version, current.UpdatedAt, current.UpdatedBy);
    }

    private static void RequireFeePercent(decimal feePercent)
    {
        if (feePercent < 0 || feePercent > 99.99m || decimal.Round(feePercent, 2) != feePercent)
            throw new WalletValidationException("INVALID_WITHDRAWAL_FEE_PERCENT", "Phí rút phải từ 0% đến 99,99%, tối đa 2 chữ số thập phân.");
    }

    public async Task<WithdrawalQuoteResponse> QuoteAsync(decimal amount, CancellationToken cancellationToken)
    {
        RequireAmountInRange(amount);
        var policy = await GetFeeSettingsAsync(cancellationToken);
        RequireFeePercent(policy.FeePercent);
        var fee = decimal.Round(amount * policy.FeePercent / 100m, 0, MidpointRounding.AwayFromZero);
        return new(amount, fee, WithdrawalRules.CalculateNetAmount(amount, fee), settings.MinimumAmount, settings.MaximumAmount, policy.FeePercent);
    }

    public async Task<WithdrawalResponse> CreateAsync(Guid userId, CreateWithdrawalRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length is < 8 or > 140)
            throw new WalletValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency key không hợp lệ.");
        var existing = await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null) return Map(RequireSameRequest(existing, request));

        RequireAmountInRange(request.Amount);

        var account = await dbContext.BankAccounts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == request.BankAccountId && item.UserId == userId, cancellationToken)
            ?? throw new WalletValidationException("BANK_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản ngân hàng.");
        if (BankCatalogue.Find(account.BankCode) is null || string.IsNullOrWhiteSpace(account.AccountHolderName))
            throw new WalletValidationException("INVALID_BANK_ACCOUNT", "Tài khoản nhận tiền cần được bổ sung thông tin ngân hàng hợp lệ.");
        try
        {
            var verifiedNumber = NormalizeAccountNumber(protector.Unprotect(account.AccountNumberEncrypted));
            if (verifiedNumber[^4..] != account.AccountNumberLast4)
                throw new WalletValidationException("INVALID_BANK_ACCOUNT", "Thông tin tài khoản nhận tiền không khớp.");
        }
        catch (Exception error) when (error is CryptographicException or FormatException or InvalidOperationException)
        { throw new WalletValidationException("INVALID_BANK_ACCOUNT", "Không thể xác minh tài khoản nhận tiền."); }
        var walletSummary = await walletService.GetOrCreateAsync(userId, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(LockIsolation, cancellationToken);
        existing = await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(RequireSameRequest(existing, request));
        }

        var wallet = await LockWallet(walletSummary.Id).SingleAsync(cancellationToken);
        await dbContext.Entry(wallet).ReloadAsync(cancellationToken);
        existing = await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null) return Map(RequireSameRequest(existing, request));
        var quote = await QuoteAsync(request.Amount, cancellationToken);
        if (request.ExpectedFee.HasValue && request.ExpectedFee.Value != quote.Fee)
            throw new WalletConflictException("WITHDRAWAL_FEE_CHANGED", "Phí rút tiền đã thay đổi. Hãy kiểm tra báo giá mới và xác nhận lại.");
        if (wallet.Status != WalletStatus.Active)
            throw new WalletConflictException("WALLET_NOT_ACTIVE", "Ví hiện không hoạt động.");
        if (wallet.Currency != "VND") throw new WalletConflictException("INVALID_WALLET_CURRENCY", "Ví không hỗ trợ rút VNĐ.");
        WalletFinancialRules.RequireSufficientBalance(wallet.AvailableBalance, request.Amount);
        WalletFinancialRules.RequireBalances(wallet.AvailableBalance, wallet.HeldBalance);
        WalletFinancialRules.RequireBalances(wallet.AvailableBalance - request.Amount, wallet.HeldBalance + request.Amount);

        var withdrawalId = Guid.NewGuid();
        var beforeAvailable = wallet.AvailableBalance;
        var beforeHeld = wallet.HeldBalance;
        wallet.AvailableBalance -= request.Amount;
        wallet.HeldBalance += request.Amount;
        WalletFinancialRules.RequireBalances(wallet.AvailableBalance, wallet.HeldBalance);
        var ledger = new WalletTransaction
        {
            WalletId = wallet.Id, Type = WalletTransactionType.Withdrawal, Amount = -request.Amount,
            BalanceBefore = beforeAvailable, BalanceAfter = wallet.AvailableBalance,
            HeldBefore = beforeHeld, HeldAfter = wallet.HeldBalance,
            ReferenceType = "Withdrawal", ReferenceId = withdrawalId.ToString("D"),
            Description = $"Rút tiền về {account.BankName} •••• {account.AccountNumberLast4}",
            Status = WalletTransactionStatus.Pending, IdempotencyKey = LedgerIdempotencyKey(userId, request.IdempotencyKey),
            Metadata = JsonSerializer.Serialize(new[] { new WithdrawalAuditEvent(WithdrawalStatus.Pending, DateTime.UtcNow, userId, null) })
        };
        var now = DateTime.UtcNow;
        var withdrawal = new Withdrawal
        {
            Id = withdrawalId, UserId = userId, WalletId = wallet.Id, BankAccountId = account.Id,
            LedgerTransactionId = ledger.Id, Amount = request.Amount, Fee = quote.Fee, FeePercent = quote.FeePercent, NetAmount = quote.NetAmount,
            TransactionCode = $"ANKT{now:yyMMddHHmm}{withdrawalId.ToString("N")[..6].ToUpperInvariant()}",
            IdempotencyKey = request.IdempotencyKey, BankCode = account.BankCode, BankName = account.BankName,
            BankAccountLast4 = account.AccountNumberLast4, BankAccountHolderName = account.AccountHolderName
        };
        dbContext.WalletTransactions.Add(ledger);
        dbContext.Withdrawals.Add(withdrawal);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(withdrawal);
    }

    public async Task<WithdrawalResponse?> GetAsync(Guid userId, Guid withdrawalId, CancellationToken cancellationToken)
    {
        var item = await dbContext.Withdrawals.AsNoTracking().Include(w => w.LedgerTransaction).SingleOrDefaultAsync(
            withdrawal => withdrawal.Id == withdrawalId && withdrawal.UserId == userId, cancellationToken);
        return item is null ? null : Map(item) with { Timeline = ReadTimeline(item.LedgerTransaction.Metadata, item.CreatedAt, item.UserId).Select(e => new WithdrawalPublicEvent(e.Status, e.At, e.Reason)).ToArray() };
    }

    public async Task<WithdrawalPage> GetPageAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.Withdrawals.AsNoTracking().Where(item => item.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(Map).ToArray(), page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<WithdrawalPage> GetAdminPageAsync(int page, int pageSize, CancellationToken cancellationToken, string? keyword = null, WithdrawalStatus? status = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.Withdrawals.AsNoTracking().Include(item => item.User).AsQueryable();
        if (status.HasValue) query = query.Where(item => item.Status == status);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            query = query.Where(item => item.TransactionCode.Contains(search) || item.User.DisplayName.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(item => Map(item) with { UserId = item.UserId, DisplayName = item.User.DisplayName }).ToArray(), page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<WithdrawalResponse> CancelAsync(Guid userId, Guid withdrawalId, CancellationToken cancellationToken)
    {
        var owned = await dbContext.Withdrawals.AsNoTracking().AnyAsync(item => item.Id == withdrawalId && item.UserId == userId, cancellationToken);
        if (!owned) throw new WalletNotFoundException();
        return await ChangeStatusAsync(withdrawalId, WithdrawalStatus.Cancelled, null, cancellationToken);
    }

    public async Task<WithdrawalResponse> ChangeStatusAsync(Guid withdrawalId, WithdrawalStatus status, string? reason, CancellationToken cancellationToken, Guid? adminId = null)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(LockIsolation, cancellationToken);
        var withdrawalQuery = dbContext.Database.IsNpgsql()
            ? dbContext.Withdrawals.FromSqlInterpolated($"SELECT * FROM \"Withdrawals\" WHERE \"Id\" = {withdrawalId} FOR UPDATE")
            : dbContext.Withdrawals.Where(item => item.Id == withdrawalId);
        var withdrawal = await withdrawalQuery.SingleOrDefaultAsync(cancellationToken)
            ?? throw new WalletNotFoundException();
        await dbContext.Entry(withdrawal).ReloadAsync(cancellationToken);
        if (withdrawal.Status == status)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(withdrawal);
        }
        WithdrawalRules.RequireTransition(withdrawal.Status, status);
        WithdrawalRules.RequireFailureReason(status, reason);
        if (status is WithdrawalStatus.Processing or WithdrawalStatus.Completed)
        {
            var recipient = await GetAdminDetailAsync(withdrawalId, cancellationToken);
            if (recipient?.CanTransfer != true)
                throw new WalletValidationException("INVALID_WITHDRAWAL_RECIPIENT", "Cần kiểm tra lại thông tin và số tiền chuyển khoản trước khi xử lý.");
        }
        var wallet = await LockWallet(withdrawal.WalletId).SingleAsync(cancellationToken);
        await dbContext.Entry(wallet).ReloadAsync(cancellationToken);
        var initialLedger = await dbContext.WalletTransactions.SingleAsync(item => item.Id == withdrawal.LedgerTransactionId, cancellationToken);
        await dbContext.Entry(initialLedger).ReloadAsync(cancellationToken);
        if (wallet.UserId != withdrawal.UserId || wallet.Currency != "VND" || initialLedger.WalletId != wallet.Id ||
            initialLedger.Type != WalletTransactionType.Withdrawal || initialLedger.Amount != -withdrawal.Amount ||
            initialLedger.ReferenceType != "Withdrawal" || initialLedger.ReferenceId != withdrawal.Id.ToString("D") ||
            initialLedger.Status != WalletTransactionStatus.Pending || initialLedger.HeldAfter - initialLedger.HeldBefore != withdrawal.Amount ||
            initialLedger.BalanceBefore - initialLedger.BalanceAfter != withdrawal.Amount)
            throw new WalletConflictException("WITHDRAWAL_LEDGER_INCONSISTENT", "Bút toán giữ tiền cần được kiểm tra trước khi xử lý yêu cầu.");
        var now = DateTime.UtcNow;
        WalletFinancialRules.RequireBalances(wallet.AvailableBalance, wallet.HeldBalance);
        if (status is WithdrawalStatus.Rejected or WithdrawalStatus.Failed or WithdrawalStatus.Cancelled)
            WalletFinancialRules.RequireBalances(wallet.AvailableBalance + withdrawal.Amount, wallet.HeldBalance - withdrawal.Amount);

        if (status == WithdrawalStatus.Processing)
        {
            withdrawal.ProcessingAt = now;
        }
        else if (status == WithdrawalStatus.Completed)
        {
            RequireHeld(wallet, withdrawal.Amount);
            AddSettlementLedger(wallet, withdrawal, WalletTransactionType.Capture, -withdrawal.Amount, now);
            wallet.HeldBalance -= withdrawal.Amount;
            initialLedger.Status = WalletTransactionStatus.Completed;
            initialLedger.CompletedAt = now;
            withdrawal.CompletedAt = now;
        }
        else
        {
            RequireHeld(wallet, withdrawal.Amount);
            AddSettlementLedger(wallet, withdrawal, WalletTransactionType.Release, withdrawal.Amount, now);
            wallet.HeldBalance -= withdrawal.Amount;
            wallet.AvailableBalance += withdrawal.Amount;
            initialLedger.Status = status == WithdrawalStatus.Cancelled ? WalletTransactionStatus.Reversed : WalletTransactionStatus.Failed;
            withdrawal.FailureReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        }
        withdrawal.Status = status;
        WalletFinancialRules.RequireBalances(wallet.AvailableBalance, wallet.HeldBalance);
        var timeline = ReadTimeline(initialLedger.Metadata, withdrawal.CreatedAt, withdrawal.UserId);
        timeline.Add(new(status, now, adminId ?? withdrawal.UserId, reason?.Trim()));
        initialLedger.Metadata = JsonSerializer.Serialize(timeline);
        if (adminId.HasValue) initialLedger.AdminId = adminId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(withdrawal);
    }

    private void RequireAmountInRange(decimal amount)
    {
        WalletFinancialRules.RequirePositiveAmount(amount);
        if (amount < settings.MinimumAmount || amount > settings.MaximumAmount)
            throw new WalletValidationException("WITHDRAWAL_AMOUNT_OUT_OF_RANGE", $"Số tiền rút phải từ {settings.MinimumAmount:N0} đến {settings.MaximumAmount:N0} VND.");
    }

    private void AddSettlementLedger(Wallet wallet, Withdrawal withdrawal, WalletTransactionType type, decimal amount, DateTime now)
    {
        var releasesFunds = type == WalletTransactionType.Release;
        dbContext.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = type,
            Amount = amount,
            BalanceBefore = wallet.AvailableBalance,
            BalanceAfter = releasesFunds ? wallet.AvailableBalance + withdrawal.Amount : wallet.AvailableBalance,
            HeldBefore = wallet.HeldBalance,
            HeldAfter = wallet.HeldBalance - withdrawal.Amount,
            ReferenceType = "Withdrawal",
            ReferenceId = withdrawal.Id.ToString("D"),
            Description = releasesFunds ? "Hoàn tiền yêu cầu rút" : "Hoàn tất yêu cầu rút",
            Status = WalletTransactionStatus.Completed,
            IdempotencyKey = $"withdrawal:{withdrawal.Id:N}:{type.ToString().ToLowerInvariant()}",
            CompletedAt = now
        });
    }

    private static string NormalizeAccountNumber(string value)
    {
        var normalized = (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (normalized.Length is < 6 or > 25 || normalized.Any(character => character is < '0' or > '9'))
            throw new WalletValidationException("INVALID_ACCOUNT_NUMBER", "Số tài khoản ngân hàng không hợp lệ.");
        return normalized;
    }

    private static string Required(string value, int maxLength, string code)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new WalletValidationException(code, "Thông tin tài khoản ngân hàng không hợp lệ.");
        return normalized;
    }

    private static void RequireHeld(Wallet wallet, decimal amount)
    {
        if (wallet.HeldBalance < amount)
            throw new WalletConflictException("WITHDRAWAL_HOLD_MISSING", "Số dư tạm giữ không đủ cho yêu cầu rút tiền.");
    }

    private static Withdrawal RequireSameRequest(Withdrawal existing, CreateWithdrawalRequest request)
    {
        if (existing.Amount != request.Amount || existing.BankAccountId != request.BankAccountId)
            throw new WalletConflictException("IDEMPOTENCY_KEY_REUSED", "Idempotency key đã được dùng cho yêu cầu khác.");
        return existing;
    }

    private static string LedgerIdempotencyKey(Guid userId, string requestKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{requestKey}"));
        return $"withdrawal:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private IsolationLevel LockIsolation => dbContext.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable;
    private IQueryable<Wallet> LockWallet(Guid id) => dbContext.Database.IsNpgsql()
        ? dbContext.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {id} FOR UPDATE")
        : dbContext.Wallets.Where(wallet => wallet.Id == id);

    private static List<WithdrawalAuditEvent> ReadTimeline(string? metadata, DateTime createdAt, Guid userId)
    {
        try { if (metadata is not null) return JsonSerializer.Deserialize<List<WithdrawalAuditEvent>>(metadata) ?? []; }
        catch (JsonException) { }
        return [new(WithdrawalStatus.Pending, createdAt, userId, null)];
    }

    public async Task<AdminWithdrawalDetail?> GetAdminDetailAsync(Guid withdrawalId, CancellationToken cancellationToken)
    {
        var item = await dbContext.Withdrawals.AsNoTracking().Include(w => w.User).Include(w => w.BankAccount).Include(w => w.LedgerTransaction)
            .SingleOrDefaultAsync(w => w.Id == withdrawalId, cancellationToken);
        if (item is null) return null;
        string? number = null;
        try { number = protector.Unprotect(item.BankAccount.AccountNumberEncrypted); }
        catch (Exception error) when (error is CryptographicException or FormatException or InvalidOperationException) { }
        // Never use a modified recipient account for an existing withdrawal snapshot.
        if (number is null || number.Length is < 6 or > 25 || number.Any(c => c is < '0' or > '9') || item.BankAccountLast4?.Length != 4 ||
            !number.EndsWith(item.BankAccountLast4, StringComparison.Ordinal) || item.BankAccount.BankCode != item.BankCode) number = null;
        var bank = BankCatalogue.Find(item.BankCode);
        var canTransfer = number is not null && bank is not null && !string.IsNullOrWhiteSpace(item.BankAccountHolderName) &&
            !string.IsNullOrWhiteSpace(item.TransactionCode) && item.TransactionCode.Length <= 25 &&
            item.BankAccount.UserId == item.UserId && item.Amount > 0 && item.Amount <= WalletFinancialRules.MaximumSafeVnd &&
            decimal.Truncate(item.Amount) == item.Amount && item.Fee >= 0 && decimal.Truncate(item.Fee) == item.Fee &&
            item.NetAmount == item.Amount - item.Fee && item.NetAmount > 0;
        var qr = canTransfer && item.Status == WithdrawalStatus.Processing
            ? $"https://img.vietqr.io/image/{bank!.Bin}-{number}-compact2.png?amount={item.NetAmount.ToString("0", CultureInfo.InvariantCulture)}&addInfo={Uri.EscapeDataString(item.TransactionCode)}&accountName={Uri.EscapeDataString(item.BankAccountHolderName)}"
            : null;
        var timeline = ReadTimeline(item.LedgerTransaction.Metadata, item.CreatedAt, item.UserId);
        var actorIds = timeline.Where(e => e.ActorId.HasValue).Select(e => e.ActorId!.Value).Distinct().ToArray();
        var names = await dbContext.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return new(Map(item), item.UserId, item.User.DisplayName, number, qr,
            timeline.Select(e => e with { ActorName = e.ActorId.HasValue ? names.GetValueOrDefault(e.ActorId.Value) : null }).ToArray(), canTransfer);
    }

    private static BankAccountResponse Map(BankAccount item) => new(item.Id, item.BankCode, item.BankName, $"•••• {item.AccountNumberLast4}", item.AccountHolderName, item.IsDefault, item.CreatedAt);
    private static WithdrawalResponse Map(Withdrawal item) => new(item.Id, item.TransactionCode, item.Amount, item.Fee, item.NetAmount, "VND", item.Status, item.BankAccountId, item.BankCode, item.BankName, $"•••• {item.BankAccountLast4}", item.BankAccountHolderName, item.FailureReason, item.CreatedAt, item.UpdatedAt, item.ProcessingAt, item.CompletedAt, FeePercent: item.FeePercent);
}
