using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Wallets;

public sealed partial class AdminFinanceService(AppDbContext db) : IAdminFinanceService
{
    private const int ExportLimit = 10000;

    // Both report views and exports use this journal. Existing unique IDs prevent replay double counting.
    private IQueryable<RevenueItem> Revenue(FinancePeriod period, string? source)
    {
        if (!string.IsNullOrEmpty(source) && source is not ("advertising" or "withdrawal_fee"))
            throw new WalletValidationException("INVALID_REVENUE_SOURCE", "Nguồn thu không hợp lệ.");
        var ads = db.WalletTransactions.AsNoTracking().Where(x => x.Status == WalletTransactionStatus.Completed &&
            x.CoinAmount == null && x.Wallet.Currency == "VND" &&
            (x.ReferenceType == "AdvertisementEvent" || x.ReferenceType == "Advertisement") &&
            (x.Type == WalletTransactionType.Capture || x.Type == WalletTransactionType.Refund) &&
            (x.CompletedAt ?? x.CreatedAt) >= period.StartUtc && (x.CompletedAt ?? x.CreatedAt) < period.EndUtc)
            .Select(x => new RevenueItem { Id = x.Id, Source = "advertising", UserId = x.Wallet.UserId, DisplayName = x.Wallet.User.DisplayName, Amount = -x.Amount,
                OccurredAt = x.CompletedAt ?? x.CreatedAt, ReferenceType = x.ReferenceType, ReferenceId = x.ReferenceId,
                Description = x.Type == WalletTransactionType.Refund ? "Hoàn tiền quảng cáo" : "Chi phí quảng cáo đã sử dụng" });
        var fees = db.Withdrawals.AsNoTracking().Where(x => x.Status == WithdrawalStatus.Completed &&
            x.CompletedAt >= period.StartUtc && x.CompletedAt < period.EndUtc)
            .Select(x => new RevenueItem { Id = x.Id, Source = "withdrawal_fee", UserId = x.UserId, DisplayName = x.User.DisplayName, Amount = x.Fee,
                OccurredAt = x.CompletedAt!.Value, ReferenceType = "Withdrawal", ReferenceId = x.Id.ToString(), Description = x.TransactionCode });
        return source == "advertising" ? ads : source == "withdrawal_fee" ? fees : ads.Concat(fees);
    }

    public async Task<AdminPage<RevenueItem>> GetRevenueAsync(DateOnly? from, DateOnly? to, string? source, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Revenue(FinancePeriod.Create(from, to), source);
        (page, pageSize) = Normalize(page, pageSize);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Source).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Page(rows, page, pageSize, total);
    }

    public async Task<string> ExportRevenueAsync(DateOnly? from, DateOnly? to, string? source, CancellationToken cancellationToken)
    {
        var rows = await Revenue(FinancePeriod.Create(from, to), source).OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.Source).ThenBy(x => x.Id).Take(ExportLimit + 1).ToListAsync(cancellationToken);
        if (rows.Count > ExportLimit)
            throw new WalletValidationException("FINANCE_EXPORT_LIMIT", "Báo cáo vượt 10.000 dòng. Hãy thu hẹp thời gian hoặc nguồn thu để xuất đầy đủ.");
        return FinanceCsv.Revenue(rows);
    }

    public async Task<FinanceOverview> GetOverviewAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var period = FinancePeriod.Create(from, to);
        // All figures within one overview share the same database snapshot.
        await using var snapshot = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
        var journal = Revenue(period, null);
        List<DailyAggregate> totals;
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // EF Core 8 SQLite cannot SUM decimal; production PostgreSQL aggregates on the server.
            var values = await journal.Select(x => new { x.OccurredAt, x.Source, x.Amount }).ToListAsync(cancellationToken);
            totals = values.GroupBy(x => new { Day = x.OccurredAt.AddHours(7).Date, x.Source })
                .Select(x => new DailyAggregate(x.Key.Day, x.Key.Source, x.Sum(v => v.Amount))).ToList();
        }
        else
            totals = await journal.GroupBy(x => new { Day = x.OccurredAt.AddHours(7).Date, x.Source })
                .Select(x => new DailyAggregate(x.Key.Day, x.Key.Source, x.Sum(v => v.Amount))).ToListAsync(cancellationToken);
        var daily = Enumerable.Range(0, period.To.DayNumber - period.From.DayNumber + 1).Select(offset =>
        {
            var day = period.From.AddDays(offset);
            return new FinanceDailyRevenue(day, totals.Where(x => DateOnly.FromDateTime(x.Day) == day && x.Source == "advertising").Sum(x => x.Amount),
                totals.Where(x => DateOnly.FromDateTime(x.Day) == day && x.Source == "withdrawal_fee").Sum(x => x.Amount));
        }).ToList();
        var deposits = await Sum(db.Payments.AsNoTracking().Where(x => x.Status == PaymentStatus.Paid && x.Currency == "VND" &&
            x.PaidAt >= period.StartUtc && x.PaidAt < period.EndUtc).Select(x => x.Amount), cancellationToken);
        var withdrawals = await Sum(db.Withdrawals.AsNoTracking().Where(x => x.Status == WithdrawalStatus.Completed &&
            x.CompletedAt >= period.StartUtc && x.CompletedAt < period.EndUtc).Select(x => x.NetAmount), cancellationToken);
        var wallets = db.Wallets.AsNoTracking().Where(x => x.Currency == "VND");
        var available = await Sum(wallets.Select(x => x.AvailableBalance), cancellationToken);
        var held = await Sum(wallets.Select(x => x.HeldBalance), cancellationToken);
        var outstanding = db.Withdrawals.AsNoTracking().Where(x => x.Status == WithdrawalStatus.Pending || x.Status == WithdrawalStatus.Processing);
        var pending = await outstanding.CountAsync(x => x.Status == WithdrawalStatus.Pending, cancellationToken);
        var processing = await outstanding.CountAsync(x => x.Status == WithdrawalStatus.Processing, cancellationToken);
        var outstandingAmount = await Sum(outstanding.Select(x => x.Amount), cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(period.From, period.To, "Asia/Ho_Chi_Minh", DateTime.UtcNow, deposits, withdrawals,
            daily.Sum(x => x.AdvertisingRevenue), daily.Sum(x => x.WithdrawalFeeRevenue), available, held, pending, processing, outstandingAmount, daily);
    }

    private async Task<decimal> Sum(IQueryable<decimal> query, CancellationToken cancellationToken) => db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
        ? (await query.ToListAsync(cancellationToken)).Sum()
        : await query.SumAsync(cancellationToken);

    private sealed record DailyAggregate(DateTime Day, string Source, decimal Amount);
    private static (int Page, int Size) Normalize(int page, int size) => (Math.Clamp(page, 1, 1000000), Math.Clamp(size, 1, 100));
    private static AdminPage<T> Page<T>(IReadOnlyList<T> rows, int page, int size, int total) => new(rows, page, size, total, (int)Math.Ceiling(total / (double)size));
}
