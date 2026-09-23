using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Wallets;
using Xunit;

namespace Viora.Application.Tests.Wallets;

public sealed class WalletHistoryIntegrationTests
{
    [Fact]
    public async Task New_deposit_has_one_pending_ledger_and_does_not_change_balance()
    {
        await using var scope = await WalletScope.CreateAsync();
        var payment = await scope.CreateDepositAsync(50_000m);

        var ledger = await scope.Db.WalletTransactions.SingleAsync();
        var wallet = await scope.Db.Wallets.SingleAsync();
        var history = await scope.HistoryAsync();
        Assert.Equal(ledger.Id, payment.TransactionId);
        Assert.Equal(WalletTransactionStatus.Pending, ledger.Status);
        Assert.Equal(0m, wallet.AvailableBalance);
        Assert.Equal(payment.Id, Assert.Single(history.Data).Id);
        Assert.Equal(PaymentStatus.Pending, history.Data[0].PaymentStatus);
    }

    [Fact]
    public async Task Paid_deposit_updates_the_same_ledger_and_credits_once()
    {
        await using var scope = await WalletScope.CreateAsync();
        var payment = await scope.CreateDepositAsync(50_000m);
        var originalLedgerId = payment.TransactionId;

        await scope.WalletService.CompleteDepositAsync(payment.ProviderOrderCode, "provider-1", 50_000m, default);
        await scope.WalletService.CompleteDepositAsync(payment.ProviderOrderCode, "provider-1", 50_000m, default);

        var wallet = await scope.Db.Wallets.AsNoTracking().SingleAsync();
        var history = await scope.HistoryAsync();
        Assert.Equal(50_000m, wallet.AvailableBalance);
        Assert.Equal(originalLedgerId, await scope.Db.Payments.AsNoTracking().Select(item => item.LedgerTransactionId).SingleAsync());
        Assert.Equal(1, await scope.Db.WalletTransactions.CountAsync());
        Assert.Equal(payment.Id, Assert.Single(history.Data).Id);
        Assert.Equal(PaymentStatus.Paid, history.Data[0].PaymentStatus);
    }

    [Fact]
    public async Task Cancelled_deposit_stays_in_history_without_credit()
    {
        await using var scope = await WalletScope.CreateAsync();
        var payment = await scope.CreateDepositAsync(50_000m);
        var cancelled = await scope.PaymentService.CancelAsync(scope.UserId, payment.Id, default);

        Assert.Equal(PaymentStatus.Cancelled, cancelled?.Status);
        var history = await scope.HistoryAsync(group: WalletHistoryGroup.Deposit);
        Assert.Equal(PaymentStatus.Cancelled, Assert.Single(history.Data).PaymentStatus);
        Assert.Equal(0m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Expired_deposit_stays_in_history_without_credit()
    {
        await using var scope = await WalletScope.CreateAsync();
        var payment = await scope.CreateDepositAsync(50_000m);
        var entity = await scope.Db.Payments.SingleAsync();
        entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await scope.Db.SaveChangesAsync();
        var expired = await scope.PaymentService.GetAsync(scope.UserId, payment.Id, default);

        Assert.Equal(PaymentStatus.Expired, expired?.Status);
        Assert.Equal(PaymentStatus.Expired, Assert.Single((await scope.HistoryAsync()).Data).PaymentStatus);
        Assert.Equal(0m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Failed_deposit_stays_in_history_without_credit()
    {
        await using var scope = await WalletScope.CreateAsync();
        var payment = await scope.CreateDepositAsync(50_000m);
        scope.Provider.Status = "FAILED";
        var failed = await scope.PaymentService.GetAsync(scope.UserId, payment.Id, default);

        Assert.Equal(PaymentStatus.Failed, failed?.Status);
        Assert.Equal(PaymentStatus.Failed, Assert.Single((await scope.HistoryAsync()).Data).PaymentStatus);
        Assert.Equal(0m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Three_distinct_deposits_create_three_history_items()
    {
        await using var scope = await WalletScope.CreateAsync();
        await scope.CreateDepositAsync(50_000m);
        await scope.CreateDepositAsync(100_000m);
        await scope.CreateDepositAsync(200_000m);

        var history = await scope.HistoryAsync(group: WalletHistoryGroup.Deposit);
        Assert.Equal(3, history.TotalItems);
        Assert.Equal(3, history.Data.Select(item => item.Id).Distinct().Count());
        Assert.Equal(3, await scope.Db.WalletTransactions.CountAsync());
        Assert.Equal(0m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).AvailableBalance);
    }

    [Fact]
    public async Task Reusing_deposit_key_with_a_different_amount_is_rejected()
    {
        await using var scope = await WalletScope.CreateAsync();
        await scope.CreateDepositAsync(50_000m, "same-key");

        await Assert.ThrowsAsync<WalletConflictException>(() => scope.CreateDepositAsync(100_000m, "same-key"));
        Assert.Equal(1, await scope.Db.Payments.CountAsync());
        Assert.Equal(1, await scope.Db.WalletTransactions.CountAsync());
    }

    [Fact]
    public async Task History_includes_hold_spend_and_only_the_unspent_advertisement_refund()
    {
        await using var scope = await WalletScope.CreateAsync();
        var deposit = await scope.CreateDepositAsync(50_000m);
        await scope.WalletService.CompleteDepositAsync(deposit.ProviderOrderCode, "provider-1", 50_000m, default);
        var reference = Guid.NewGuid().ToString("D");

        await scope.WalletService.HoldAsync(scope.UserId, 50_000m, "Advertisement", reference, "ad:hold", default);
        await scope.WalletService.CaptureAsync(scope.UserId, 18_500m, "AdvertisementEvent", reference, "ad:spend", default);
        await scope.WalletService.ReleaseAsync(scope.UserId, 31_500m, "Advertisement", reference, "ad:release", default);
        await scope.WalletService.ReleaseAsync(scope.UserId, 31_500m, "Advertisement", reference, "ad:release", default);

        var wallet = await scope.Db.Wallets.AsNoTracking().SingleAsync();
        var history = await scope.HistoryAsync();
        Assert.Equal(31_500m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.HeldBalance);
        Assert.Equal(4, history.TotalItems);
        Assert.Contains(history.Data, item => item.Type == WalletTransactionType.Hold && item.Amount == -50_000m);
        Assert.Contains(history.Data, item => item.Type == WalletTransactionType.Capture && item.Amount == -18_500m);
        Assert.Contains(history.Data, item => item.Type == WalletTransactionType.Release && item.Amount == 31_500m);
        Assert.Single((await scope.HistoryAsync(group: WalletHistoryGroup.Refund)).Data);
    }

    [Fact]
    public async Task Legacy_payment_reference_does_not_duplicate_its_ledger_in_history()
    {
        await using var scope = await WalletScope.CreateAsync();
        await scope.CreateDepositAsync(50_000m);
        var payment = await scope.Db.Payments.SingleAsync();
        payment.LedgerTransactionId = null;
        await scope.Db.SaveChangesAsync();

        var history = await scope.HistoryAsync();
        Assert.Single(history.Data);
        Assert.Equal(payment.Id, history.Data[0].Id);
        Assert.True(history.Data[0].HasBalanceSnapshot);
    }

    [Fact]
    public async Task Legacy_payment_without_a_ledger_still_appears_as_a_deposit()
    {
        await using var scope = await WalletScope.CreateAsync();
        await scope.CreateDepositAsync(50_000m);
        var payment = await scope.Db.Payments.SingleAsync();
        payment.LedgerTransactionId = null;
        await scope.Db.SaveChangesAsync();
        scope.Db.WalletTransactions.Remove(await scope.Db.WalletTransactions.SingleAsync());
        await scope.Db.SaveChangesAsync();

        var history = await scope.HistoryAsync(group: WalletHistoryGroup.Deposit);
        Assert.Equal(payment.Id, Assert.Single(history.Data).Id);
        Assert.False(history.Data[0].HasBalanceSnapshot);
    }

    [Fact]
    public async Task Deposit_detail_expires_an_overdue_payment_without_credit()
    {
        await using var scope = await WalletScope.CreateAsync();
        var payment = await scope.CreateDepositAsync(50_000m);
        var entity = await scope.Db.Payments.SingleAsync();
        entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await scope.Db.SaveChangesAsync();

        var detail = await scope.WalletService.GetTransactionAsync(scope.UserId, payment.Id, default);
        Assert.Equal(PaymentStatus.Expired, detail?.PaymentStatus);
        Assert.Equal(0m, (await scope.Db.Wallets.AsNoTracking().SingleAsync()).AvailableBalance);
    }

    private sealed class WalletScope : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }
        public WalletService WalletService { get; }
        public PaymentService PaymentService { get; }
        public FakePayOsClientFactory Provider { get; }
        public Guid UserId { get; }

        private WalletScope(SqliteConnection connection, AppDbContext db, Guid userId)
        {
            this.connection = connection;
            Db = db;
            UserId = userId;
            WalletService = new WalletService(db);
            Provider = new FakePayOsClientFactory();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PAYOS_CLIENT_ID"] = "test-client",
                ["PAYOS_API_KEY"] = "test-api-key",
                ["PAYOS_CHECKSUM_KEY"] = "test-checksum"
            }).Build();
            PaymentService = new PaymentService(db, WalletService, Provider, configuration,
                NullLogger<PaymentService>.Instance);
        }

        public static async Task<WalletScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction("char_length", (string value) => value.Length);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var account = new Account { Email = "wallet-test@example.com" };
            var user = new User { AccountId = account.Id, DisplayName = "Wallet tester" };
            var wallet = new Wallet { UserId = user.Id };
            db.Accounts.Add(account);
            db.Users.Add(user);
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();
            return new WalletScope(connection, db, user.Id);
        }

        public Task<PaymentResponse> CreateDepositAsync(decimal amount, string? key = null) =>
            PaymentService.CreateDepositAsync(UserId,
                new CreateDepositRequest(amount, "https://app.test/return", "https://app.test/cancel", key ?? Guid.NewGuid().ToString("N")),
                default);

        public Task<WalletTransactionPage> HistoryAsync(WalletTransactionType? type = null, WalletHistoryGroup? group = null) =>
            WalletService.GetTransactionsAsync(UserId, 1, 20, type, group, default);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakePayOsClientFactory : IHttpClientFactory
    {
        public string Status { get; set; } = "PENDING";
        public HttpClient CreateClient(string name) => new(new FakePayOsHandler(this))
        {
            BaseAddress = new Uri("https://payos.test/")
        };
    }

    private sealed class FakePayOsHandler : HttpMessageHandler
    {
        private readonly FakePayOsClientFactory provider;

        public FakePayOsHandler(FakePayOsClientFactory provider) => this.provider = provider;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var orderCode = long.TryParse(path.Split('/').Reverse().Skip(path.EndsWith("/cancel") ? 1 : 0).First(), out var parsed)
                ? parsed : 0;
            var data = request.Method == HttpMethod.Get || path.EndsWith("/cancel")
                ? JsonSerializer.Serialize(new { orderCode, status = path.EndsWith("/cancel") ? "CANCELLED" : provider.Status, amount = 50_000m, id = "provider-test" })
                : JsonSerializer.Serialize(new
            {
                paymentLinkId = Guid.NewGuid().ToString("N"),
                checkoutUrl = "https://payos.test/checkout",
                qrCode = "test-qr-code"
            });
            var signature = PayOsSignature.CreateWebhookSignature(data, "test-checksum");
            var body = $"{{\"code\":\"00\",\"data\":{data},\"signature\":\"{signature}\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
