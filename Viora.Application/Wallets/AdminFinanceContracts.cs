using System.Globalization;

namespace Viora.Application.Wallets;

public sealed record FinancePeriod(DateOnly From, DateOnly To, DateTime StartUtc, DateTime EndUtc)
{
    public static FinancePeriod Create(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;
        if (end < start || end.DayNumber - start.DayNumber >= 366 || end == DateOnly.MaxValue || start == DateOnly.MinValue)
            throw new WalletValidationException("INVALID_FINANCE_PERIOD", "Chọn khoảng thời gian hợp lệ, tối đa 366 ngày.");
        return new(start, end, DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc),
            DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc));
    }
}

public sealed record RevenueItem
{
    public Guid Id { get; init; }
    public string Source { get; init; } = "";
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = "";
    public decimal Amount { get; init; }
    public DateTime OccurredAt { get; init; }
    public string ReferenceType { get; init; } = "";
    public string ReferenceId { get; init; } = "";
    public string Description { get; init; } = "";
}
public sealed record FinanceDailyRevenue(DateOnly Date, decimal AdvertisingRevenue, decimal WithdrawalFeeRevenue)
{
    public decimal TotalRevenue => AdvertisingRevenue + WithdrawalFeeRevenue;
}
public sealed record FinanceOverview(DateOnly From, DateOnly To, string TimeZone, DateTime GeneratedAt,
    decimal DepositedAmount, decimal WithdrawnNetAmount, decimal AdvertisingRevenue, decimal WithdrawalFeeRevenue,
    decimal AvailableBalance, decimal HeldBalance, int PendingWithdrawals, int ProcessingWithdrawals,
    decimal OutstandingWithdrawalAmount, IReadOnlyList<FinanceDailyRevenue> Daily)
{
    public decimal TotalRevenue => AdvertisingRevenue + WithdrawalFeeRevenue;
}
public sealed record FinanceReconciliationIssue
{
    public string Code { get; init; } = "";
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public string DisplayName { get; init; } = "";
    public string ReferenceType { get; init; } = "";
    public string ReferenceId { get; init; } = "";
    public decimal ExpectedAmount { get; init; }
    public decimal ActualAmount { get; init; }
    public DateTime DetectedAt { get; init; }
}

public interface IAdminFinanceService
{
    Task<FinanceOverview> GetOverviewAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
    Task<AdminPage<RevenueItem>> GetRevenueAsync(DateOnly? from, DateOnly? to, string? source, int page, int pageSize, CancellationToken cancellationToken);
    Task<string> ExportRevenueAsync(DateOnly? from, DateOnly? to, string? source, CancellationToken cancellationToken);
    Task<AdminPage<FinanceReconciliationIssue>> GetReconciliationAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public static class FinanceCsv
{
    public static string Cell(string? value)
    {
        value ??= "";
        var trimmed = value.TrimStart();
        if (value.Length > 0 && (value[0] is '\t' or '\r' or '\n' || trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Revenue(IEnumerable<RevenueItem> rows) =>
        "Mã giao dịch,Nguồn thu,Người dùng,Số tiền VNĐ,Thời gian UTC,Loại tham chiếu,Mã tham chiếu,Nội dung\r\n" +
        string.Join("\r\n", rows.Select(x => string.Join(",", new[] {
            Cell(x.Id.ToString()), Cell(x.Source), Cell(x.DisplayName), x.Amount.ToString("0", CultureInfo.InvariantCulture),
            Cell(x.OccurredAt.ToString("O", CultureInfo.InvariantCulture)), Cell(x.ReferenceType), Cell(x.ReferenceId), Cell(x.Description)
        })));
}
