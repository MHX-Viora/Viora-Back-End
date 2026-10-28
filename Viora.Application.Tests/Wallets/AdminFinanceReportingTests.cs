using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using viora_BE.Controllers.Admin;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Wallets;
using Xunit;

public sealed class AdminFinanceReportingTests
{
    private static readonly DateOnly Day = new(2026, 10, 28);

    [Fact]
    public void VietnamRangeIsInclusiveAndBounded()
    {
        var range = FinancePeriod.Create(Day, Day);
        Assert.Equal(new DateTime(2026, 10, 27, 17, 0, 0, DateTimeKind.Utc), range.StartUtc);
        Assert.Equal(range.StartUtc.AddDays(1), range.EndUtc);
        Assert.Throws<WalletValidationException>(() => FinancePeriod.Create(Day, Day.AddDays(-1)));
        Assert.Throws<WalletValidationException>(() => FinancePeriod.Create(Day, Day.AddDays(366)));
    }

    [Fact]
    public async Task RevenueIncludesOnlySettledFeesAndAdConsumptionWithRefunds()
    {
        await using var f = await Fixture.Create();
        await f.Wallet.HoldAsync(f.UserId, 10000, "Advertisement", "ad", "hold", default);
        await f.Wallet.CaptureAsync(f.UserId, 3000, "AdvertisementEvent", "ad", "ad-charge", default);
        await f.Wallet.RefundAsync(f.UserId, 500, "AdvertisementEvent", "ad", "ad-refund", default);
        await f.Wallet.ReleaseAsync(f.UserId, 7000, "Advertisement", "ad", "release", default);
        await f.Wallet.AdjustAsync(f.UserId, f.UserId, 1000, "Correction", "adjust", default);
        var pending = await f.Withdrawals.CreateAsync(f.UserId, new(50000, f.BankId, "finance-pending-request"), default);
        var completed = await f.Withdrawals.CreateAsync(f.UserId, new(50000, f.BankId, "finance-completed-request"), default);
        await f.Withdrawals.ChangeStatusAsync(completed.Id, WithdrawalStatus.Processing, null, default);
        await f.Withdrawals.ChangeStatusAsync(completed.Id, WithdrawalStatus.Completed, null, default);
        await f.Withdrawals.ChangeStatusAsync(completed.Id, WithdrawalStatus.Completed, null, default);
        await f.Withdrawals.UpdateFeeSettingsAsync(f.UserId, 25, 1, default);
        await f.MoveToDay();
        var report = await f.Reports.GetOverviewAsync(Day, Day, default);
        Assert.Equal(2500m, report.AdvertisingRevenue);
        Assert.Equal(5000m, report.WithdrawalFeeRevenue);
        Assert.Equal(7500m, report.TotalRevenue);
        Assert.Equal(45000m, report.WithdrawnNetAmount);
        Assert.Equal(1, report.PendingWithdrawals);
        Assert.Single(report.Daily);
        Assert.Equal(report.TotalRevenue, report.Daily.Sum(x => x.TotalRevenue));
        var journal = await f.Reports.GetRevenueAsync(Day, Day, null, 1, 20, default);
        Assert.Equal(3, journal.TotalItems);
        Assert.Equal(report.TotalRevenue, journal.Data.Sum(x => x.Amount));
        Assert.DoesNotContain(journal.Data, x => x.Id == pending.Id);
        var fees = await f.Reports.GetRevenueAsync(Day, Day, "withdrawal_fee", 1, 1, default);
        Assert.Single(fees.Data);
        Assert.Equal(5000m, fees.Data[0].Amount);
        Assert.Empty((await f.Reports.GetRevenueAsync(Day, Day, "withdrawal_fee", 2, 1, default)).Data);
    }

    [Fact]
    public async Task DateBoundaryUsesCompletionTimeAndExcludesNextVietnamDay()
    {
        await using var f = await Fixture.Create();
        await f.Wallet.HoldAsync(f.UserId, 10000, "Advertisement", "ad", "hold", default);
        await f.Wallet.CaptureAsync(f.UserId, 1000, "AdvertisementEvent", "ad", "first", default);
        await f.Wallet.CaptureAsync(f.UserId, 2000, "AdvertisementEvent", "ad", "second", default);
        var rows = (await f.Db.WalletTransactions.Where(x => x.Type == WalletTransactionType.Capture).ToListAsync()).OrderBy(x => x.Amount).ToList();
        rows[0].CompletedAt = new DateTime(2026, 10, 28, 17, 0, 0, DateTimeKind.Utc);
        rows[1].CompletedAt = new DateTime(2026, 10, 27, 17, 0, 0, DateTimeKind.Utc);
        await f.Db.SaveChangesAsync();
        var report = await f.Reports.GetRevenueAsync(Day, Day, null, 1, 20, default);
        Assert.Single(report.Data);
        Assert.Equal(1000m, report.Data[0].Amount);
    }

    [Fact]
    public async Task LiveTransactionsAreHiddenBeforePaginationButRemainInWalletReconciliation()
    {
        await using var f = await Fixture.Create();
        var wallet = await f.Db.Wallets.SingleAsync();
        f.Db.WalletTransactions.Add(new WalletTransaction { WalletId = wallet.Id, Type = WalletTransactionType.LiveGiftReceived,
            Amount = 1000, BalanceBefore = 200000, BalanceAfter = 201000, HeldBefore = 0, HeldAfter = 0,
            Status = WalletTransactionStatus.Completed, ReferenceType = "LiveGift", ReferenceId = "gift", IdempotencyKey = "gift" });
        wallet.AvailableBalance = 201000;
        await f.Db.SaveChangesAsync();
        var list = await new AdminWalletService(f.Db).GetTransactionsAsync(1, 1, null, default);
        Assert.Equal(0, list.TotalItems);
        var reconciliation = await f.Reports.GetReconciliationAsync(1, 20, default);
        Assert.Equal(0, reconciliation.TotalItems);
    }

    [Fact]
    public async Task ReconciliationReportsUncreditedDepositAndWalletDriftWithoutRepair()
    {
        await using var f = await Fixture.Create();
        await f.Wallet.AdjustAsync(f.UserId, f.UserId, 1000, "Correction", "adjust", default);
        var wallet = await f.Db.Wallets.SingleAsync();
        wallet.AvailableBalance += 100;
        f.Db.Payments.Add(new Payment { UserId = f.UserId, WalletId = wallet.Id, Amount = 10000, Purpose = "WalletDeposit",
            ReferenceType = "Wallet", ReferenceId = wallet.Id.ToString(), Provider = "payOS", ProviderOrderCode = 123,
            Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow, IdempotencyKey = "deposit" });
        await f.Db.SaveChangesAsync();
        var result = await f.Reports.GetReconciliationAsync(1, 20, default);
        Assert.Contains(result.Data, x => x.Code == "PAID_DEPOSIT_UNCREDITED");
        Assert.Contains(result.Data, x => x.Code == "WALLET_BALANCE_MISMATCH");
        Assert.Equal(201100m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        var report = await f.Reports.GetOverviewAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), default);
        Assert.Equal(10000m, report.DepositedAmount);
        Assert.Equal(0m, report.TotalRevenue);
    }

    [Fact]
    public async Task RejectsUnknownRevenueSourceAndEscapesCsvCells()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<WalletValidationException>(() => f.Reports.GetRevenueAsync(Day, Day, "gift", 1, 20, default));
        Assert.Equal("\"'=SUM(1,2)\"", FinanceCsv.Cell("=SUM(1,2)"));
        Assert.Equal("\"'\t=1\"", FinanceCsv.Cell("\t=1"));
        Assert.Equal("\"a\"\"b\"", FinanceCsv.Cell("a\"b"));
    }

    [Fact]
    public async Task TransactionFiltersRunBeforePaginationAndRejectWrappedStatuses()
    {
        await using var f = await Fixture.Create();
        await f.Wallet.AdjustAsync(f.UserId, f.UserId, 1000, "Correction", "adjust-filter", default);
        await f.MoveToDay();
        var admin = new AdminWalletService(f.Db);
        var match = await admin.GetTransactionsAsync(1, 20, "Finance", default, new(Type: WalletTransactionType.Adjustment, Status: 1));
        Assert.Single(match.Data);
        Assert.Empty((await admin.GetTransactionsAsync(1, 20, null, default, new(Type: WalletTransactionType.Deposit))).Data);
        await Assert.ThrowsAsync<WalletValidationException>(() => admin.GetTransactionsAsync(1, 20, null, default, new(Status: 65536)));
        await Assert.ThrowsAsync<WalletValidationException>(() => admin.GetTransactionsAsync(1, 20, null, default, new(Type: WalletTransactionType.LiveGiftSent)));
        await Assert.ThrowsAsync<WalletValidationException>(() => admin.GetPaymentsAsync(1, 20, null, default, new(Status: 65536)));
    }

    [Fact]
    public async Task ExportUsesSameFilteredJournalAndEscapesUserControlledNames()
    {
        await using var f = await Fixture.Create();
        (await f.Db.Users.SingleAsync()).DisplayName = "=SUM(1,2)";
        await f.Db.SaveChangesAsync();
        await f.Wallet.HoldAsync(f.UserId, 1000, "Advertisement", "ad", "export-hold", default);
        await f.Wallet.CaptureAsync(f.UserId, 1000, "AdvertisementEvent", "ad", "export-charge", default);
        await f.MoveToDay();
        var csv = await f.Reports.ExportRevenueAsync(Day, Day, "advertising", default);
        var journal = await f.Reports.GetRevenueAsync(Day, Day, "advertising", 1, 20, default);
        Assert.Contains(journal.Data.Single().Id.ToString(), csv);
        Assert.Contains("\"'=SUM(1,2)\"", csv);
        Assert.DoesNotContain("withdrawal_fee", csv);
    }

    [Fact]
    public void PostgreSqlCanTranslateRevenueJournalAndDailyGroupingWithoutConnecting()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=finance_translation_test;Username=test;Password=test").Options);
        var service = new AdminFinanceService(db);
        var method = typeof(AdminFinanceService).GetMethod("Revenue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var query = (IQueryable<RevenueItem>)method.Invoke(service, new object?[] { FinancePeriod.Create(Day, Day), null })!;
        Assert.Contains("UNION ALL", query.OrderBy(x => x.OccurredAt).Take(20).ToQueryString());
        var sql = query.GroupBy(x => new { Day = x.OccurredAt.AddHours(7).Date, x.Source })
            .Select(x => new { x.Key.Day, x.Key.Source, Amount = x.Sum(v => v.Amount) }).ToQueryString();
        Assert.Contains("sum(", sql, StringComparison.OrdinalIgnoreCase);
        IQueryable<FinanceReconciliationIssue> Reconcile(string name) => (IQueryable<FinanceReconciliationIssue>)typeof(AdminFinanceService)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object[] { DateTime.UtcNow })!;
        var reconciliationSql = Reconcile("ReconciliationBase").Concat(Reconcile("AdvertisementReconciliation")).OrderBy(x => x.Code).Take(20).ToQueryString();
        Assert.Contains("UNION ALL", reconciliationSql);
        Assert.Contains("WalletTransactions", reconciliationSql);
    }

    [Fact]
    public async Task ReportHttpEndpointsRequireAdminValidateFiltersAndDoNotCache()
    {
        await using var f = await Fixture.Create();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IAdminFinanceService>(f.Reports);
        builder.Services.AddSingleton<IAdminWalletService>(new AdminWalletService(f.Db));
        builder.Services.AddSingleton<IWalletService>(f.Wallet);
        builder.Services.AddSingleton<IWithdrawalService>(f.Withdrawals);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, Auth>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminFinanceReportsController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(); using var client = app.GetTestClient();
        var routes = new[] { "overview", "revenue", "revenue/export", "reconciliation", "transactions", "withdrawals" };
        foreach (var route in routes) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/finance/" + route)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Fixture-Role", "0");
        foreach (var route in routes) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/finance/" + route)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Fixture-Role"); client.DefaultRequestHeaders.Add("X-Fixture-Role", "2");
        foreach (var route in routes)
        {
            var response = await client.GetAsync("/api/admin/finance/" + route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/admin/finance/revenue?source=gift")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/admin/finance/overview?from=2026-10-28&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/admin/finance/transactions?status=65536")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/finance/revenue?from=wrong-date")).StatusCode);
        var csv = await client.GetAsync("/api/admin/finance/revenue/export?from=2026-10-01&to=2026-10-28");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("2026-10-01-2026-10-28", csv.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(new byte[] { 239, 187, 191 }, (await csv.Content.ReadAsByteArrayAsync()).Take(3));
    }

    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Fixture-Role"].ToString();
            return Task.FromResult(string.IsNullOrEmpty(role) ? AuthenticateResult.NoResult() : AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "fixture")), "fixture")));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required AppDbContext Db { get; init; }
        public required WalletService Wallet { get; init; }
        public required WithdrawalService Withdrawals { get; init; }
        public required AdminFinanceService Reports { get; init; }
        public Guid UserId { get; init; }
        public Guid BankId { get; init; }
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction("char_length", (string value) => value.Length);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var account = new Account { Email = "finance@example.test" };
            var user = new User { AccountId = account.Id, DisplayName = "Finance user" };
            db.AddRange(account, user, new Wallet { UserId = user.Id, AvailableBalance = 200000 });
            await db.SaveChangesAsync();
            var wallet = new WalletService(db);
            var options = Options.Create(new WithdrawalOptions { BankAccountEncryptionKey = Convert.ToBase64String(new byte[32]) });
            var withdrawals = new WithdrawalService(db, wallet, new BankAccountProtector(options), options);
            var bank = await withdrawals.CreateBankAccountAsync(user.Id, new("VCB", "", "1234567890", "FINANCE USER", true), default);
            return new() { Connection = connection, Db = db, Wallet = wallet, Withdrawals = withdrawals,
                Reports = new AdminFinanceService(db), UserId = user.Id, BankId = bank.Id };
        }
        public async Task MoveToDay()
        {
            foreach (var row in await Db.WalletTransactions.ToListAsync()) if (row.CompletedAt.HasValue) row.CompletedAt = new DateTime(2026, 10, 28, 2, 0, 0, DateTimeKind.Utc);
            foreach (var row in await Db.Withdrawals.ToListAsync()) if (row.CompletedAt.HasValue) row.CompletedAt = new DateTime(2026, 10, 28, 2, 0, 0, DateTimeKind.Utc);
            await Db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
