using System.Data;
using System.Security.Cryptography;
using System.Text;
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
        var bankCode = Required(request.BankCode, 30, "INVALID_BANK_CODE").ToUpperInvariant();
        var bankName = Required(request.BankName, 120, "INVALID_BANK_NAME");
        var holder = Required(request.AccountHolderName, 120, "INVALID_ACCOUNT_HOLDER").ToUpperInvariant();
        var hash = protector.Hash(number);
        if (await dbContext.BankAccounts.AnyAsync(item => item.UserId == userId && item.AccountNumberHash == hash, cancellationToken))
            throw new WalletConflictException("BANK_ACCOUNT_EXISTS", "Tài khoản ngân hàng đã tồn tại.");

        var makeDefault = request.IsDefault || !await dbContext.BankAccounts.AnyAsync(item => item.UserId == userId, cancellationToken);
        if (makeDefault)
        {
            var defaults = await dbContext.BankAccounts.Where(item => item.UserId == userId && item.IsDefault).ToListAsync(cancellationToken);
            foreach (var item in defaults) item.IsDefault = false;
        }

        var account = new BankAccount
        {
            UserId = userId, BankCode = bankCode, BankName = bankName,
            AccountNumberEncrypted = protector.Protect(number), AccountNumberHash = hash,
            AccountNumberLast4 = number[^4..], AccountHolderName = holder, IsDefault = makeDefault
        };
        dbContext.BankAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(account);
    }

    public WithdrawalQuoteResponse Quote(decimal amount)
    {
        RequireAmountInRange(amount);
        return new(amount, settings.Fee, WithdrawalRules.CalculateNetAmount(amount, settings.Fee), settings.MinimumAmount, settings.MaximumAmount);
    }

    public async Task<WithdrawalResponse> CreateAsync(Guid userId, CreateWithdrawalRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length is < 8 or > 140)
            throw new WalletValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency key không hợp lệ.");
        var quote = Quote(request.Amount);
        var existing = await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null) return Map(RequireSameRequest(existing, request));

        var account = await dbContext.BankAccounts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == request.BankAccountId && item.UserId == userId, cancellationToken)
            ?? throw new WalletValidationException("BANK_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản ngân hàng.");
        var walletSummary = await walletService.GetOrCreateAsync(userId, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        existing = await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(RequireSameRequest(existing, request));
        }

        var wallet = await dbContext.Wallets
            .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {walletSummary.Id} FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (wallet.Status != WalletStatus.Active)
            throw new WalletConflictException("WALLET_NOT_ACTIVE", "Ví hiện không hoạt động.");
        WalletFinancialRules.RequireSufficientBalance(wallet.AvailableBalance, request.Amount);

        var withdrawalId = Guid.NewGuid();
        var beforeAvailable = wallet.AvailableBalance;
        var beforeHeld = wallet.HeldBalance;
        wallet.AvailableBalance -= request.Amount;
        wallet.HeldBalance += request.Amount;
        var ledger = new WalletTransaction
        {
            WalletId = wallet.Id, Type = WalletTransactionType.Withdrawal, Amount = -request.Amount,
            BalanceBefore = beforeAvailable, BalanceAfter = wallet.AvailableBalance,
            HeldBefore = beforeHeld, HeldAfter = wallet.HeldBalance,
            ReferenceType = "Withdrawal", ReferenceId = withdrawalId.ToString("D"),
            Description = $"Rút tiền về {account.BankName} •••• {account.AccountNumberLast4}",
            Status = WalletTransactionStatus.Pending, IdempotencyKey = LedgerIdempotencyKey(userId, request.IdempotencyKey)
        };
        var now = DateTime.UtcNow;
        var withdrawal = new Withdrawal
        {
            Id = withdrawalId, UserId = userId, WalletId = wallet.Id, BankAccountId = account.Id,
            LedgerTransactionId = ledger.Id, Amount = request.Amount, Fee = quote.Fee, NetAmount = quote.NetAmount,
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
        var item = await dbContext.Withdrawals.AsNoTracking().SingleOrDefaultAsync(
            withdrawal => withdrawal.Id == withdrawalId && withdrawal.UserId == userId, cancellationToken);
        return item is null ? null : Map(item);
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

    public async Task<WithdrawalPage> GetAdminPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.Withdrawals.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items.Select(Map).ToArray(), page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<WithdrawalResponse> CancelAsync(Guid userId, Guid withdrawalId, CancellationToken cancellationToken)
    {
        var owned = await dbContext.Withdrawals.AsNoTracking().AnyAsync(item => item.Id == withdrawalId && item.UserId == userId, cancellationToken);
        if (!owned) throw new WalletNotFoundException();
        return await ChangeStatusAsync(withdrawalId, WithdrawalStatus.Cancelled, null, cancellationToken);
    }

    public async Task<WithdrawalResponse> ChangeStatusAsync(Guid withdrawalId, WithdrawalStatus status, string? reason, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var withdrawal = await dbContext.Withdrawals
            .FromSqlInterpolated($"SELECT * FROM \"Withdrawals\" WHERE \"Id\" = {withdrawalId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WalletNotFoundException();
        if (withdrawal.Status == status)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(withdrawal);
        }
        WithdrawalRules.RequireTransition(withdrawal.Status, status);
        WithdrawalRules.RequireFailureReason(status, reason);
        var wallet = await dbContext.Wallets
            .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"Id\" = {withdrawal.WalletId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var initialLedger = await dbContext.WalletTransactions.SingleAsync(item => item.Id == withdrawal.LedgerTransactionId, cancellationToken);
        var now = DateTime.UtcNow;

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
        if (normalized.Length is < 6 or > 25 || normalized.Any(character => !char.IsDigit(character)))
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

    private static BankAccountResponse Map(BankAccount item) => new(item.Id, item.BankCode, item.BankName, $"•••• {item.AccountNumberLast4}", item.AccountHolderName, item.IsDefault, item.CreatedAt);
    private static WithdrawalResponse Map(Withdrawal item) => new(item.Id, item.TransactionCode, item.Amount, item.Fee, item.NetAmount, "VND", item.Status, item.BankAccountId, item.BankCode, item.BankName, $"•••• {item.BankAccountLast4}", item.BankAccountHolderName, item.FailureReason, item.CreatedAt, item.UpdatedAt, item.ProcessingAt, item.CompletedAt);
}
