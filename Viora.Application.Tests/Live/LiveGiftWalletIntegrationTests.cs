using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.LiveStreaming;
using Viora.Infrastructure.Persistence;
using Xunit;

namespace Viora.Application.Tests.Live;

// SQLite verifies persisted transfer invariants and rollback; it does not prove PostgreSQL row locking.
public sealed class LiveGiftWalletIntegrationTests
{
    [Fact]
    public async Task Quantity_three_transfers_vnd_and_records_both_balance_snapshots_without_changing_coin()
    {
        await using var scope = await GiftScope.CreateAsync();
        var requestId = Guid.NewGuid();

        var result = await scope.SendAsync(3, requestId);

        Assert.False(result.IsReplay);
        Assert.Equal(70_000m, result.SenderBalance);
        Assert.Equal(35_000m, result.ReceiverBalance);
        scope.Db.ChangeTracker.Clear();
        var sender = await scope.Db.Wallets.SingleAsync(x => x.UserId == scope.SenderId);
        var receiver = await scope.Db.Wallets.SingleAsync(x => x.UserId == scope.ReceiverId);
        Assert.Equal(70_000m, sender.AvailableBalance);
        Assert.Equal(35_000m, receiver.AvailableBalance);
        Assert.Equal(123L, sender.AnktCoinBalance);
        Assert.Equal(456L, receiver.AnktCoinBalance);
        Assert.Equal(2_000m, sender.HeldBalance);
        Assert.Equal(1_000m, receiver.HeldBalance);

        var giftTransaction = await scope.Db.LiveGiftTransactions.SingleAsync();
        Assert.Equal(requestId, giftTransaction.RequestId);
        Assert.Equal(3, giftTransaction.Quantity);
        Assert.Equal(10_000L, giftTransaction.UnitPrice);
        Assert.Equal(30_000L, giftTransaction.GrossAmount);
        Assert.Equal(30_000L, giftTransaction.NetAmount);
        Assert.Equal(0L, giftTransaction.FeeAmount);
        Assert.Equal("VND", giftTransaction.Currency);
        Assert.Equal("Rose", giftTransaction.GiftName);
        Assert.Equal(scope.SenderId, giftTransaction.SenderUserId);
        Assert.Equal(scope.ReceiverId, giftTransaction.HostUserId);
        var ledgers = await scope.Db.WalletTransactions.ToListAsync();
        Assert.Equal(2, ledgers.Count);
        var debit = Assert.Single(ledgers, x => x.WalletId == sender.Id);
        var credit = Assert.Single(ledgers, x => x.WalletId == receiver.Id);
        Assert.Equal(giftTransaction.WalletTransactionId, debit.Id);
        Assert.Equal(giftTransaction.ReceiverWalletTransactionId, credit.Id);
        Assert.Equal((WalletTransactionType)10, debit.Type);
        Assert.Equal((WalletTransactionType)11, credit.Type);
        Assert.Equal(-30_000m, debit.Amount);
        Assert.Equal(30_000m, credit.Amount);
        Assert.Equal(100_000m, debit.BalanceBefore);
        Assert.Equal(70_000m, debit.BalanceAfter);
        Assert.Equal(5_000m, credit.BalanceBefore);
        Assert.Equal(35_000m, credit.BalanceAfter);
        Assert.Equal(2_000m, debit.HeldBefore);
        Assert.Equal(2_000m, debit.HeldAfter);
        Assert.Equal(1_000m, credit.HeldBefore);
        Assert.Equal(1_000m, credit.HeldAfter);
        Assert.NotEqual(debit.IdempotencyKey, credit.IdempotencyKey);
        foreach (var ledger in ledgers)
        {
            Assert.Equal(WalletTransactionStatus.Completed, ledger.Status);
            Assert.NotNull(ledger.CompletedAt);
            Assert.Null(ledger.CoinAmount);
            Assert.Equal("LiveGift", ledger.ReferenceType);
            Assert.Equal(giftTransaction.Id.ToString("D"), ledger.ReferenceId);
            using var metadata = JsonDocument.Parse(ledger.Metadata!);
            Assert.Equal(scope.LiveId, metadata.RootElement.GetProperty("liveId").GetGuid());
            Assert.Equal(scope.GiftId, metadata.RootElement.GetProperty("giftId").GetGuid());
            Assert.Equal("Rose", metadata.RootElement.GetProperty("giftName").GetString());
            Assert.Equal(10_000L, metadata.RootElement.GetProperty("giftPrice").GetInt64());
            Assert.Equal(3, metadata.RootElement.GetProperty("quantity").GetInt32());
            Assert.Equal(scope.SenderId, metadata.RootElement.GetProperty("senderUserId").GetGuid());
            Assert.Equal(scope.ReceiverId, metadata.RootElement.GetProperty("receiverUserId").GetGuid());
            Assert.Equal(giftTransaction.Id, metadata.RootElement.GetProperty("giftTransactionId").GetGuid());
        }
        var live = await scope.Db.Lives.SingleAsync();
        // Legacy counters remain Coin totals; VND totals come from persisted VND transfers.
        Assert.Equal(0L, live.TotalGiftCount);
        Assert.Equal(0L, live.TotalGiftValue);
    }

    [Fact]
    public async Task Identical_request_is_replayed_without_debit_or_credit_twice()
    {
        await using var scope = await GiftScope.CreateAsync();
        var requestId = Guid.NewGuid();
        var first = await scope.SendAsync(3, requestId);
        scope.Db.ChangeTracker.Clear();

        var replay = await scope.SendAsync(3, requestId);

        Assert.True(replay.IsReplay);
        Assert.Equal(first.Transaction.Id, replay.Transaction.Id);
        Assert.Equal(first.SenderBalance, replay.SenderBalance);
        Assert.Equal(first.ReceiverBalance, replay.ReceiverBalance);
        await scope.AssertCountsAsync(1, 2);
        Assert.Equal(70_000m, await scope.SenderBalanceAsync());
        Assert.Equal(35_000m, await scope.ReceiverBalanceAsync());
        Assert.Equal(0L, await scope.Db.Lives.Select(x => x.TotalGiftCount).SingleAsync());
    }

    [Fact]
    public async Task Reusing_request_with_different_quantity_is_rejected_without_another_transfer()
    {
        await using var scope = await GiftScope.CreateAsync();
        var requestId = Guid.NewGuid();
        await scope.SendAsync(3, requestId);

        var error = await Assert.ThrowsAsync<WalletConflictException>(() => scope.SendAsync(1, requestId));

        Assert.Equal("GIFT_REQUEST_REUSED", error.Code);
        await scope.AssertCountsAsync(1, 2);
        Assert.Equal(70_000m, await scope.SenderBalanceAsync());
        Assert.Equal(35_000m, await scope.ReceiverBalanceAsync());
    }

    [Fact]
    public async Task Insufficient_vnd_is_rejected_even_with_coin_and_no_state_is_changed()
    {
        await using var scope = await GiftScope.CreateAsync();
        var error = await Assert.ThrowsAsync<InsufficientWalletBalanceException>(() => scope.SendAsync(11));

        Assert.Equal("INSUFFICIENT_BALANCE", error.Code);
        await scope.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData("gift-inactive", "LIVE_GIFT_UNAVAILABLE")]
    [InlineData("gift-missing", "LIVE_GIFT_UNAVAILABLE")]
    [InlineData("live-ended", "LIVE_GIFT_UNAVAILABLE")]
    [InlineData("gifts-disabled", "LIVE_GIFT_UNAVAILABLE")]
    [InlineData("sender-suspended", "WALLET_UNAVAILABLE")]
    [InlineData("receiver-suspended", "WALLET_UNAVAILABLE")]
    [InlineData("sender-missing", "WALLET_UNAVAILABLE")]
    [InlineData("receiver-missing", "WALLET_UNAVAILABLE")]
    public async Task Unavailable_gift_live_or_wallet_never_creates_a_partial_transfer(string scenario, string code)
    {
        await using var scope = await GiftScope.CreateAsync();
        var sender = await scope.Db.Wallets.SingleAsync(x => x.UserId == scope.SenderId);
        var receiver = await scope.Db.Wallets.SingleAsync(x => x.UserId == scope.ReceiverId);
        switch (scenario)
        {
            case "gift-inactive": (await scope.Db.LiveGifts.SingleAsync()).IsActive = false; break;
            case "gift-missing": scope.GiftId = Guid.NewGuid(); break;
            case "live-ended": (await scope.Db.Lives.SingleAsync()).Status = LiveStatus.Ended; break;
            case "gifts-disabled": (await scope.Db.Lives.SingleAsync()).AllowGifts = false; break;
            case "sender-suspended": sender.Status = WalletStatus.Suspended; break;
            case "receiver-suspended": receiver.Status = WalletStatus.Suspended; break;
            case "sender-missing": scope.Db.Wallets.Remove(sender); break;
            case "receiver-missing": scope.Db.Wallets.Remove(receiver); break;
        }
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<WalletConflictException>(() => scope.SendAsync(3));

        Assert.Equal(code, error.Code);
        await scope.AssertCountsAsync(0, 0);
        foreach (var wallet in await scope.Db.Wallets.AsNoTracking().ToListAsync())
            Assert.Equal(wallet.UserId == scope.SenderId ? 100_000m : 5_000m, wallet.AvailableBalance);
        Assert.Equal(0L, await scope.Db.Lives.Select(x => x.TotalGiftValue).SingleAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Nonpositive_quantity_is_rejected_before_mutation(int quantity)
    {
        await using var scope = await GiftScope.CreateAsync();
        var error = await Assert.ThrowsAsync<WalletValidationException>(() => scope.SendAsync(quantity));
        Assert.Equal("LIVE_GIFT_INVALID", error.Code);
        await scope.AssertUnchangedAsync();
    }

    [Fact]
    public async Task Failure_after_save_rolls_back_both_wallets_ledgers_and_live_totals()
    {
        var failure = new FailAfterSaveInterceptor();
        await using var scope = await GiftScope.CreateAsync(failure);
        failure.Armed = true;

        await Assert.ThrowsAsync<InjectedSaveException>(() => scope.SendAsync(3));

        scope.Db.ChangeTracker.Clear();
        await scope.AssertUnchangedAsync();
        // A rollback must release the transaction and allow a subsequent genuine transfer.
        var retry = await scope.SendAsync(3);
        Assert.False(retry.IsReplay);
        await scope.AssertCountsAsync(1, 2);
    }

    internal sealed class GiftScope(SqliteConnection connection, AppDbContext db, Guid senderId, Guid receiverId,
        Guid liveId, Guid giftId) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public Guid SenderId { get; } = senderId;
        public Guid ReceiverId { get; } = receiverId;
        public Guid LiveId { get; } = liveId;
        public Guid GiftId { get; set; } = giftId;

        public static async Task<GiftScope> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction("char_length", (string value) => value.Length);
            var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var db = new AppDbContext(builder.Options);
            await db.Database.EnsureCreatedAsync();
            var ids = await SeedAsync(db);
            return new GiftScope(connection, db, ids.SenderId, ids.ReceiverId, ids.LiveId, ids.GiftId);
        }

        public static async Task<(Guid SenderId, Guid ReceiverId, Guid LiveId, Guid GiftId)> SeedAsync(AppDbContext db)
        {
            var senderAccount = new Account { Email = "sender@example.com" };
            var receiverAccount = new Account { Email = "receiver@example.com" };
            var sender = new User { AccountId = senderAccount.Id, DisplayName = "Sender" };
            var receiver = new User { AccountId = receiverAccount.Id, DisplayName = "Streamer" };
            var category = new LiveCategory { Name = "Chat", Slug = "chat" };
            var live = new Viora.Domain.Entities.Live
            {
                HostUserId = receiver.Id, CategoryId = category.Id, Title = "Test live",
                AgoraChannelName = "test-live", Status = LiveStatus.Live
            };
            var gift = new LiveGift { Name = "Rose", ImageUrl = "/rose.png", Price = 10_000, PriceCoin = 1 };
            db.AddRange(senderAccount, receiverAccount, sender, receiver, category, live, gift,
                new Wallet { UserId = sender.Id, AvailableBalance = 100_000m, HeldBalance = 2_000m, AnktCoinBalance = 123 },
                new Wallet { UserId = receiver.Id, AvailableBalance = 5_000m, HeldBalance = 1_000m, AnktCoinBalance = 456 });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return (sender.Id, receiver.Id, live.Id, gift.Id);
        }

        public Task<LiveGiftPaymentResult> SendAsync(int quantity, Guid? requestId = null) =>
            new LiveGiftWalletService(Db).SendAsync(SenderId, LiveId, GiftId, quantity, requestId ?? Guid.NewGuid(), default);

        public Task<decimal> SenderBalanceAsync() => Db.Wallets.AsNoTracking()
            .Where(x => x.UserId == SenderId).Select(x => x.AvailableBalance).SingleAsync();
        public Task<decimal> ReceiverBalanceAsync() => Db.Wallets.AsNoTracking()
            .Where(x => x.UserId == ReceiverId).Select(x => x.AvailableBalance).SingleAsync();

        public async Task AssertCountsAsync(int gifts, int ledgers)
        {
            Assert.Equal(gifts, await Db.LiveGiftTransactions.CountAsync());
            Assert.Equal(ledgers, await Db.WalletTransactions.CountAsync());
        }

        public async Task AssertUnchangedAsync()
        {
            await AssertCountsAsync(0, 0);
            Assert.Equal(100_000m, await SenderBalanceAsync());
            Assert.Equal(5_000m, await ReceiverBalanceAsync());
            var live = await Db.Lives.AsNoTracking().SingleAsync();
            Assert.Equal(0L, live.TotalGiftValue);
            Assert.Equal(0L, live.TotalGiftCount);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task PostgreSQL_competing_gifts_on_different_lives_cannot_overdraw_the_same_wallet()
    {
        await using var scope = await PostgresScope.CreateAsync();
        await using var setup = scope.NewContext();
        var account = new Account { Email = "third@example.com" };
        var host = new User { AccountId = account.Id, DisplayName = "Third host" };
        var categoryId = await setup.Lives.Select(x => x.CategoryId).SingleAsync();
        var otherLive = new Viora.Domain.Entities.Live
        {
            HostUserId = host.Id, CategoryId = categoryId, Title = "Other live",
            AgoraChannelName = "other-live", Status = LiveStatus.Live
        };
        setup.AddRange(account, host, new Wallet { UserId = host.Id }, otherLive);
        await setup.SaveChangesAsync();

        var outcomes = await scope.RaceAsync(
            db => new LiveGiftWalletService(db).SendAsync(scope.SenderId, scope.LiveId, scope.GiftId, 6, Guid.NewGuid(), default),
            db => new LiveGiftWalletService(db).SendAsync(scope.SenderId, otherLive.Id, scope.GiftId, 6, Guid.NewGuid(), default));

        Assert.Single(outcomes, x => x.Payment is not null);
        Assert.IsType<InsufficientWalletBalanceException>(Assert.Single(outcomes, x => x.Error is not null).Error);
        Assert.Equal(40_000m, await setup.Wallets.AsNoTracking().Where(x => x.UserId == scope.SenderId)
            .Select(x => x.AvailableBalance).SingleAsync());
        Assert.Equal(1, await setup.LiveGiftTransactions.CountAsync());
        Assert.Equal(2, await setup.WalletTransactions.CountAsync());
        Assert.Equal(105_000m, (await setup.Wallets.AsNoTracking().ToListAsync()).Sum(x => x.AvailableBalance));
    }

    [PostgresFact]
    public async Task PostgreSQL_simultaneous_identical_requests_transfer_exactly_once()
    {
        await using var scope = await PostgresScope.CreateAsync();
        var requestId = Guid.NewGuid();
        var outcomes = await scope.RaceAsync(
            db => new LiveGiftWalletService(db).SendAsync(scope.SenderId, scope.LiveId, scope.GiftId, 3, requestId, default),
            db => new LiveGiftWalletService(db).SendAsync(scope.SenderId, scope.LiveId, scope.GiftId, 3, requestId, default));

        Assert.All(outcomes, x => Assert.Null(x.Error));
        Assert.Single(outcomes, x => x.Payment!.IsReplay);
        Assert.Single(outcomes, x => !x.Payment!.IsReplay);
        Assert.Equal(outcomes[0].Payment!.Transaction.Id, outcomes[1].Payment!.Transaction.Id);
        await using var db = scope.NewContext();
        Assert.Equal(1, await db.LiveGiftTransactions.CountAsync());
        Assert.Equal(2, await db.WalletTransactions.CountAsync());
        Assert.Equal(70_000m, await db.Wallets.Where(x => x.UserId == scope.SenderId).Select(x => x.AvailableBalance).SingleAsync());
        Assert.Equal(35_000m, await db.Wallets.Where(x => x.UserId == scope.ReceiverId).Select(x => x.AvailableBalance).SingleAsync());
    }

    [PostgresFact]
    public async Task PostgreSQL_opposing_transfers_do_not_deadlock_or_lose_money()
    {
        await using var scope = await PostgresScope.CreateAsync();
        await using var db = scope.NewContext();
        var receiver = await db.Wallets.SingleAsync(x => x.UserId == scope.ReceiverId);
        receiver.AvailableBalance = 100_000m;
        var reverseLive = new Viora.Domain.Entities.Live
        {
            HostUserId = scope.SenderId, CategoryId = await db.Lives.Select(x => x.CategoryId).SingleAsync(),
            Title = "Reverse live", AgoraChannelName = "reverse-live", Status = LiveStatus.Live
        };
        db.Lives.Add(reverseLive);
        await db.SaveChangesAsync();

        var outcomes = await scope.RaceAsync(
            context => new LiveGiftWalletService(context).SendAsync(scope.SenderId, scope.LiveId, scope.GiftId, 6, Guid.NewGuid(), default),
            context => new LiveGiftWalletService(context).SendAsync(scope.ReceiverId, reverseLive.Id, scope.GiftId, 6, Guid.NewGuid(), default));

        Assert.All(outcomes, x => Assert.Null(x.Error));
        Assert.Equal(2, await db.LiveGiftTransactions.CountAsync());
        Assert.Equal(4, await db.WalletTransactions.CountAsync());
        Assert.All(await db.Wallets.AsNoTracking().ToListAsync(), x => Assert.Equal(100_000m, x.AvailableBalance));
    }

    [PostgresFact]
    public async Task PostgreSQL_wallet_hold_reloads_pretracked_balance_after_a_gift_commits()
    {
        await using var scope = await PostgresScope.CreateAsync();
        await using var staleContext = scope.NewContext();
        var staleWallet = await staleContext.Wallets.SingleAsync(x => x.UserId == scope.SenderId);
        Assert.Equal(100_000m, staleWallet.AvailableBalance);
        await using (var giftContext = scope.NewContext())
            await new LiveGiftWalletService(giftContext).SendAsync(scope.SenderId, scope.LiveId, scope.GiftId, 3, Guid.NewGuid(), default);

        await new Viora.Infrastructure.Wallets.WalletService(staleContext).HoldAsync(scope.SenderId,
            20_000m, "Advertisement", Guid.NewGuid().ToString("D"), "post-gift-hold", default);

        await using var verification = scope.NewContext();
        var wallet = await verification.Wallets.SingleAsync(x => x.UserId == scope.SenderId);
        Assert.Equal(50_000m, wallet.AvailableBalance);
        Assert.Equal(22_000m, wallet.HeldBalance);
        Assert.Equal(123L, wallet.AnktCoinBalance);
        Assert.Equal(35_000m, await verification.Wallets.Where(x => x.UserId == scope.ReceiverId)
            .Select(x => x.AvailableBalance).SingleAsync());
        var hold = await verification.WalletTransactions.SingleAsync(x => x.Type == WalletTransactionType.Hold);
        Assert.Equal(70_000m, hold.BalanceBefore);
        Assert.Equal(50_000m, hold.BalanceAfter);
    }

    [PostgresFact]
    public async Task PostgreSQL_upgrade_preserves_legacy_coin_balances_and_does_not_invent_vnd_history()
    {
        await using var scope = await PostgresScope.CreateAsync();
        await using var db = scope.NewContext();
        var wallet = await db.Wallets.SingleAsync(x => x.UserId == scope.SenderId);
        var ledger = new WalletTransaction
        {
            WalletId = wallet.Id, Type = (WalletTransactionType)8, Amount = 0,
            BalanceBefore = 100_000m, BalanceAfter = 100_000m, HeldBefore = 2_000m, HeldAfter = 2_000m,
            CoinAmount = -3, CoinBalanceBefore = 126, CoinBalanceAfter = 123,
            ReferenceType = "LiveGift", ReferenceId = "legacy-coin", IdempotencyKey = "legacy-coin",
            Status = WalletTransactionStatus.Completed, CompletedAt = DateTime.UtcNow
        };
        var historicalGift = new LiveGiftTransaction
        {
            RequestId = Guid.NewGuid(), LiveId = scope.LiveId, GiftId = scope.GiftId,
            SenderUserId = scope.SenderId, HostUserId = scope.ReceiverId,
            WalletTransactionId = ledger.Id, Quantity = 3, UnitPriceCoin = 1, TotalCoin = 3,
            HostEarning = 3, Currency = "COIN", Status = WalletTransactionStatus.Completed
        };
        db.AddRange(ledger, historicalGift);
        await db.SaveChangesAsync();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("AddLiveGiftVndWalletTransfers", migrations[^1]);
        var migrator = db.GetService<IMigrator>();
        // Reconstruct the immediately preceding schema in this disposable test database only.
        await migrator.MigrateAsync(migrations[^2]);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var wallets = await db.Wallets.AsNoTracking().ToListAsync();
        Assert.Equal(123L, wallets.Single(x => x.UserId == scope.SenderId).AnktCoinBalance);
        Assert.Equal(456L, wallets.Single(x => x.UserId == scope.ReceiverId).AnktCoinBalance);
        Assert.Equal(100_000m, wallets.Single(x => x.UserId == scope.SenderId).AvailableBalance);
        Assert.Equal(5_000m, wallets.Single(x => x.UserId == scope.ReceiverId).AvailableBalance);
        var history = await db.LiveGiftTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(historicalGift.Id, history.Id);
        Assert.Equal("COIN", history.Currency);
        Assert.Equal(3L, history.TotalCoin);
        Assert.Null(history.UnitPrice);
        Assert.Null(history.GrossAmount);
        Assert.Null(history.NetAmount);
        Assert.Null(history.FeeAmount);
        Assert.Null(history.ReceiverWalletTransactionId);
        Assert.Equal(-3L, (await db.WalletTransactions.AsNoTracking().SingleAsync()).CoinAmount);
        Assert.Equal(1_000L, await db.LiveGifts.Where(x => x.Id == scope.GiftId).Select(x => x.Price).SingleAsync());
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIVE_GIFT_TEST_POSTGRES_ADMIN")))
                Skip = "Set LIVE_GIFT_TEST_POSTGRES_ADMIN to an isolated test PostgreSQL server with CREATEDB permission.";
        }
    }

    private sealed class PostgresScope(string adminConnection, string databaseName, string connectionString,
        Guid senderId, Guid receiverId, Guid liveId, Guid giftId) : IAsyncDisposable
    {
        public Guid SenderId { get; } = senderId;
        public Guid ReceiverId { get; } = receiverId;
        public Guid LiveId { get; } = liveId;
        public Guid GiftId { get; } = giftId;
        public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, options => options.CommandTimeout(20)).Options);

        public static async Task<PostgresScope> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable("LIVE_GIFT_TEST_POSTGRES_ADMIN")!;
            var databaseName = "live_gift_test_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            // The identifier is exclusively a fixed prefix and server-generated hex UUID.
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection))
                await command.ExecuteNonQueryAsync();
            var builder = new NpgsqlConnectionStringBuilder(admin) { Database = databaseName, Pooling = false };
            try
            {
                await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(builder.ConnectionString).Options);
                await db.Database.MigrateAsync();
                var ids = await GiftScope.SeedAsync(db);
                return new PostgresScope(admin, databaseName, builder.ConnectionString,
                    ids.SenderId, ids.ReceiverId, ids.LiveId, ids.GiftId);
            }
            catch
            {
                await DropDatabaseAsync(admin, databaseName);
                throw;
            }
        }

        public async Task<(LiveGiftPaymentResult? Payment, Exception? Error)[]> RaceAsync(
            params Func<AppDbContext, Task<LiveGiftPaymentResult>>[] operations)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyCount = 0;
            async Task<(LiveGiftPaymentResult?, Exception?)> RunAsync(Func<AppDbContext, Task<LiveGiftPaymentResult>> operation)
            {
                await using var db = NewContext();
                await db.Database.OpenConnectionAsync();
                if (Interlocked.Increment(ref readyCount) == operations.Length) ready.SetResult();
                await gate.Task;
                try { return (await operation(db), null); }
                catch (Exception error) { return (null, error); }
            }
            var tasks = operations.Select(RunAsync).ToArray();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            gate.SetResult();
            return await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(40));
        }

        public ValueTask DisposeAsync() => new(DropDatabaseAsync(adminConnection, databaseName));

        private static async Task DropDatabaseAsync(string admin, string name)
        {
            if (!name.StartsWith("live_gift_test_", StringComparison.Ordinal) ||
                !Guid.TryParseExact(name["live_gift_test_".Length..], "N", out _))
                throw new InvalidOperationException("Unexpected disposable database name.");
            if (Environment.GetEnvironmentVariable("LIVE_GIFT_TEST_DISPOSABLE_CLUSTER") == "1")
            {
                var configuration = new NpgsqlConnectionStringBuilder(admin);
                if (configuration.Host is not ("127.0.0.1" or "localhost" or "::1"))
                    throw new InvalidOperationException("Disposable cluster cleanup requires a local test server.");
                // The harness owns this entire temporary cluster and removes it after the test run.
                // PostgreSQL 18 on Windows can stall DROP DATABASE on a WAL writer signal barrier.
                return;
            }
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class InjectedSaveException : Exception;

    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Armed = false;
            throw new InjectedSaveException();
        }
    }
}
