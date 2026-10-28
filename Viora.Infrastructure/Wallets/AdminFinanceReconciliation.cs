using Microsoft.EntityFrameworkCore;
using System.Data;
using Viora.Application.Wallets;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Wallets;

public sealed partial class AdminFinanceService
{
    private IQueryable<FinanceReconciliationIssue> ReconciliationBase(DateTime now)
    {
        // Includes Live internally. Hiding gift rows must not invalidate the wallet's last balance snapshot.
        var wallets = db.Wallets.AsNoTracking().Where(x => x.Currency == "VND").Select(w => new
        {
            Wallet = w,
            Available = db.WalletTransactions.Where(t => t.WalletId == w.Id && t.CoinAmount == null &&
                (t.Status == WalletTransactionStatus.Completed || t.Type == WalletTransactionType.Withdrawal))
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Select(t => (decimal?)t.BalanceAfter).FirstOrDefault(),
            Held = db.WalletTransactions.Where(t => t.WalletId == w.Id && t.CoinAmount == null &&
                (t.Status == WalletTransactionStatus.Completed || t.Type == WalletTransactionType.Withdrawal))
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Select(t => (decimal?)t.HeldAfter).FirstOrDefault()
        });
        var balances = wallets.Where(x => x.Wallet.AvailableBalance < 0 || x.Wallet.HeldBalance < 0 ||
            x.Available != null && (x.Wallet.AvailableBalance != x.Available || x.Wallet.HeldBalance != x.Held))
            .Select(x => new FinanceReconciliationIssue { Code = x.Wallet.AvailableBalance != x.Available ? "WALLET_BALANCE_MISMATCH" : "WALLET_HELD_MISMATCH", Id = x.Wallet.Id,
                UserId = x.Wallet.UserId, DisplayName = x.Wallet.User.DisplayName, ReferenceType = "Wallet", ReferenceId = x.Wallet.UserId.ToString(),
                ExpectedAmount = x.Wallet.AvailableBalance != x.Available ? x.Available ?? 0 : x.Held ?? 0,
                ActualAmount = x.Wallet.AvailableBalance != x.Available ? x.Wallet.AvailableBalance : x.Wallet.HeldBalance, DetectedAt = now });
        var deposits = db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Paid && p.Currency == "VND" &&
            !db.WalletTransactions.Any(t => t.WalletId == p.WalletId && t.Type == WalletTransactionType.Deposit &&
                t.Status == WalletTransactionStatus.Completed && t.Amount == p.Amount && t.ReferenceType == "Payment" && t.ReferenceId == p.Id.ToString()))
            .Select(x => new FinanceReconciliationIssue { Code = "PAID_DEPOSIT_UNCREDITED", Id = x.Id, UserId = x.UserId,
                DisplayName = x.User.DisplayName, ReferenceType = "Payment", ReferenceId = x.Id.ToString(),
                ExpectedAmount = x.Amount, ActualAmount = 0m, DetectedAt = now });
        var withdrawals = db.Withdrawals.AsNoTracking().Where(w => w.Status == WithdrawalStatus.Completed &&
            (!w.CompletedAt.HasValue || w.NetAmount != w.Amount - w.Fee || w.Fee < 0 || w.NetAmount <= 0 ||
            !db.WalletTransactions.Any(t => t.WalletId == w.WalletId && t.Type == WalletTransactionType.Capture &&
                t.Status == WalletTransactionStatus.Completed && t.Amount == -w.Amount && t.ReferenceType == "Withdrawal" && t.ReferenceId == w.Id.ToString())))
            .Select(x => new FinanceReconciliationIssue { Code = "WITHDRAWAL_SETTLEMENT_MISMATCH", Id = x.Id,
                UserId = x.UserId, DisplayName = x.User.DisplayName, ReferenceType = "Withdrawal", ReferenceId = x.Id.ToString(),
                ExpectedAmount = x.Amount, ActualAmount = db.WalletTransactions.Any(t => t.WalletId == x.WalletId &&
                    t.ReferenceType == "Withdrawal" && t.ReferenceId == x.Id.ToString() && t.Type == WalletTransactionType.Capture &&
                    t.Status == WalletTransactionStatus.Completed && t.Amount == -x.Amount) ? x.NetAmount + x.Fee : 0m, DetectedAt = now });
        var reservation = db.Withdrawals.AsNoTracking().Where(w =>
            (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing) &&
            !db.WalletTransactions.Any(t => t.Id == w.LedgerTransactionId && t.WalletId == w.WalletId &&
                t.Type == WalletTransactionType.Withdrawal && t.Status == WalletTransactionStatus.Pending && t.Amount == -w.Amount &&
                t.ReferenceType == "Withdrawal" && t.ReferenceId == w.Id.ToString()))
            .Select(x => new FinanceReconciliationIssue { Code = "WITHDRAWAL_RESERVATION_MISMATCH", Id = x.Id,
                UserId = x.UserId, DisplayName = x.User.DisplayName, ReferenceType = "Withdrawal", ReferenceId = x.Id.ToString(),
                ExpectedAmount = x.Amount, ActualAmount = 0m, DetectedAt = now });
        return balances.Concat(deposits).Concat(withdrawals).Concat(reservation);
    }

    public async Task<AdminPage<FinanceReconciliationIssue>> GetReconciliationAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var now = DateTime.UtcNow;
        await using var snapshot = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
        var common = ReconciliationBase(now);
        // SQLite has no decimal SUM translation; this branch is only used by local integration tests.
        IQueryable<FinanceReconciliationIssue> ads;
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var charges = await db.WalletTransactions.AsNoTracking().Include(t => t.Wallet).Where(t => t.Type == WalletTransactionType.Capture &&
                t.Status == WalletTransactionStatus.Completed && t.ReferenceType == "AdvertisementEvent").ToListAsync(cancellationToken);
            var campaigns = await db.Advertisements.AsNoTracking().Include(x => x.Advertiser).ToListAsync(cancellationToken);
            ads = campaigns.Select(x => new FinanceReconciliationIssue { Code = "ADVERTISEMENT_SPEND_MISMATCH", Id = x.Id,
                UserId = x.AdvertiserId, DisplayName = x.Advertiser.DisplayName, ReferenceType = "Advertisement", ReferenceId = x.Id.ToString(),
                ExpectedAmount = x.SpentAmount, ActualAmount = -charges.Where(t => t.Wallet.UserId == x.AdvertiserId && t.ReferenceId == x.Id.ToString()).Sum(t => t.Amount), DetectedAt = now })
                .Where(x => x.ExpectedAmount != x.ActualAmount).AsQueryable();
            var all = (await common.ToListAsync(cancellationToken)).Concat(ads)
                .OrderBy(x => x.Code).ThenBy(x => x.Id).ToList();
            await snapshot.CommitAsync(cancellationToken);
            return Page(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, all.Count);
        }
        var query = common.Concat(AdvertisementReconciliation(now));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return Page(rows, page, pageSize, total);
    }

    private IQueryable<FinanceReconciliationIssue> AdvertisementReconciliation(DateTime now) =>
        db.Advertisements.AsNoTracking().Select(a => new
        {
            Ad = a,
            Captured = -(db.WalletTransactions.Where(t => t.Wallet.UserId == a.AdvertiserId && t.ReferenceId == a.Id.ToString() &&
                t.ReferenceType == "AdvertisementEvent" && t.Type == WalletTransactionType.Capture && t.Status == WalletTransactionStatus.Completed)
                .Sum(t => (decimal?)t.Amount) ?? 0m)
        }).Where(x => x.Ad.SpentAmount != x.Captured).Select(x => new FinanceReconciliationIssue {
            Code = "ADVERTISEMENT_SPEND_MISMATCH", Id = x.Ad.Id, UserId = x.Ad.AdvertiserId, DisplayName = x.Ad.Advertiser.DisplayName,
            ReferenceType = "Advertisement", ReferenceId = x.Ad.Id.ToString(), ExpectedAmount = x.Ad.SpentAmount, ActualAmount = x.Captured, DetectedAt = now });
}
