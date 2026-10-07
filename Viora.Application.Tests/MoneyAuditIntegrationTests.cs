using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Wallets;
using Xunit;

public sealed class MoneyAuditIntegrationTests
{
    [Fact] public async Task VietQrUnsupportedAccountLengthCannotBeSaved()
    {
        await using var f = await Fixture.Create();
        var error = await Assert.ThrowsAsync<WalletValidationException>(() => f.Service.CreateBankAccountAsync(f.UserId,
            new("VCB", "ignored", "12345678901234567890", "TEST USER", true), default));
        Assert.Equal("INVALID_ACCOUNT_NUMBER", error.Code);
        Assert.Single(await f.Db.BankAccounts.ToListAsync());
    }

    [Fact] public async Task InvalidProcessingRecipientCanFailAndRefundExactlyOnce()
    {
        await using var f = await Fixture.Create();
        var request = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        var admin = Guid.NewGuid();
        await f.Service.ChangeStatusAsync(request.Id, WithdrawalStatus.Processing, null, default, admin);
        var account = await f.Db.BankAccounts.SingleAsync(a => a.Id == f.BankId);
        var protector = new BankAccountProtector(Options.Create(new WithdrawalOptions { BankAccountEncryptionKey = Convert.ToBase64String(new byte[32]) }));
        account.AccountNumberEncrypted = protector.Protect("12345678901234567890");
        account.AccountNumberLast4 = "7890";
        await f.Db.SaveChangesAsync();
        var detail = await f.Service.GetAdminDetailAsync(request.Id, default);
        Assert.False(detail!.CanTransfer);
        Assert.Null(detail.QrImageUrl);
        await Assert.ThrowsAsync<WalletValidationException>(() => f.Service.ChangeStatusAsync(request.Id, WithdrawalStatus.Failed, " ", default, admin));
        const string reason = "Thông tin tài khoản không hợp lệ";
        await f.Service.ChangeStatusAsync(request.Id, WithdrawalStatus.Failed, reason, default, admin);
        await f.Service.ChangeStatusAsync(request.Id, WithdrawalStatus.Failed, reason, default, admin);
        var wallet = await f.Db.Wallets.SingleAsync();
        Assert.Equal(100000m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.HeldBalance);
        Assert.Equal(2, await f.Db.WalletTransactions.CountAsync());
        var failed = await f.Service.GetAdminDetailAsync(request.Id, default);
        Assert.Equal(WithdrawalStatus.Failed, failed!.Withdrawal.Status);
        Assert.Equal(reason, failed.Withdrawal.FailureReason);
        Assert.Equal(admin, failed.Timeline.Last().ActorId);
    }

    [Fact] public async Task SavesWithdrawalRecipientWithVietnameseNameAndEncryptsAccountNumber()
    {
        await using var f = await Fixture.Create();
        const string number = "35346464634643643";
        var saved = await f.Service.CreateBankAccountAsync(f.UserId, new("VCB", "ignored", number, "Trịnh Trọng Quyền", true), default);
        var stored = await f.Db.BankAccounts.SingleAsync(a => a.Id == saved.Id);
        var protector = new BankAccountProtector(Options.Create(new WithdrawalOptions { BankAccountEncryptionKey = Convert.ToBase64String(new byte[32]) }));
        Assert.Equal(number, protector.Unprotect(stored.AccountNumberEncrypted));
        Assert.NotEqual(number, stored.AccountNumberEncrypted);
        Assert.Equal("•••• 3643", saved.AccountNumberMasked);
        Assert.Equal("TRỊNH TRỌNG QUYỀN", saved.AccountHolderName);
        Assert.Single(await f.Db.BankAccounts.Where(a => a.IsDefault).ToListAsync());
        Assert.Equal(100000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
    }

    [Fact] public async Task WithdrawalReservesOnceAndReplayMustMatch()
    {
        await using var f = await Fixture.Create();
        var request = f.Request(50000);
        var first = await f.Service.CreateAsync(f.UserId, request, default);
        Assert.Equal(first.Id, (await f.Service.CreateAsync(f.UserId, request, default)).Id);
        Assert.Equal(50000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Equal(50000m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
        Assert.Single(await f.Db.WalletTransactions.ToListAsync());
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Service.CreateAsync(f.UserId, request with { Amount = 60000 }, default));
    }
    [Fact] public async Task CompletionConsumesHeldOnceAndAuditsAdmin()
    {
        await using var f = await Fixture.Create();
        var withdrawal = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        var admin = Guid.NewGuid();
        await f.Service.ChangeStatusAsync(withdrawal.Id, WithdrawalStatus.Processing, null, default, admin);
        await f.Service.ChangeStatusAsync(withdrawal.Id, WithdrawalStatus.Completed, null, default, admin);
        await f.Service.ChangeStatusAsync(withdrawal.Id, WithdrawalStatus.Completed, null, default, admin);
        Assert.Equal(50000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Equal(0m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
        Assert.Equal(2, await f.Db.WalletTransactions.CountAsync());
        var detail = await f.Service.GetAdminDetailAsync(withdrawal.Id, default);
        Assert.Equal(admin, detail!.Timeline.Last().ActorId);
        Assert.Null(detail.QrImageUrl);
    }
    [Theory] [InlineData(WithdrawalStatus.Rejected)] [InlineData(WithdrawalStatus.Cancelled)]
    public async Task RejectOrCancelReleasesFullAmountOnce(WithdrawalStatus status)
    {
        await using var f = await Fixture.Create();
        var w = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        await f.Service.ChangeStatusAsync(w.Id, status, "Đã từ chối", default);
        await f.Service.ChangeStatusAsync(w.Id, status, "Đã từ chối", default);
        Assert.Equal(100000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Equal(0m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
        Assert.Equal(2, await f.Db.WalletTransactions.CountAsync());
    }
    [Fact] public async Task MissingOrForeignBankCannotReserveMoney()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<WalletValidationException>(() => f.Service.CreateAsync(f.UserId, f.Request(50000) with { BankAccountId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<WalletValidationException>(() => f.Service.CreateAsync(Guid.NewGuid(), f.Request(50000), default));
        Assert.Equal(100000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
    }
    [Fact] public async Task UserDetailIsOwnedAndMaskedAdminQrUsesNet()
    {
        await using var f = await Fixture.Create();
        var w = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        Assert.Null(await f.Service.GetAsync(Guid.NewGuid(), w.Id, default));
        Assert.DoesNotContain("1234567890", w.BankAccountMasked);
        var detail = await f.Service.GetAdminDetailAsync(w.Id, default);
        Assert.Null(detail!.QrImageUrl);
        await f.Service.ChangeStatusAsync(w.Id, WithdrawalStatus.Processing, null, default, Guid.NewGuid());
        detail = await f.Service.GetAdminDetailAsync(w.Id, default);
        Assert.Equal("1234567890", detail!.AccountNumber);
        Assert.Contains("amount=45000", detail.QrImageUrl!);
        Assert.Contains(w.TransactionCode, detail.QrImageUrl!);
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Service.CancelAsync(f.UserId, w.Id, default));
        Assert.Equal(50000m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
    }
    [Fact] public async Task WalletReplayCannotChangeAmountSignOrOwner()
    {
        await using var f = await Fixture.Create();
        var admin = Guid.NewGuid();
        await f.Wallet.AdjustAsync(f.UserId, admin, 1000, "Correction", "audit-adjust", default);
        await f.Wallet.AdjustAsync(f.UserId, admin, 1000, "Correction", "audit-adjust", default);
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Wallet.AdjustAsync(f.UserId, admin, -1000, "Correction", "audit-adjust", default));
        Assert.Equal(101000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
    }
    [Fact] public async Task CombinedWithdrawalsCannotOverdraw()
    {
        await using var f = await Fixture.Create();
        await f.Service.CreateAsync(f.UserId, f.Request(60000), default);
        await Assert.ThrowsAsync<InsufficientWalletBalanceException>(() => f.Service.CreateAsync(f.UserId, f.Request(50000), default));
        Assert.Equal(40000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Equal(60000m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
    }
    [Fact] public async Task ConfirmedDepositCreditsOnceAndRejectsMismatchedAmount()
    {
        await using var f = await Fixture.Create();
        var wallet = await f.Db.Wallets.SingleAsync();
        var payment = new Payment { UserId = f.UserId, WalletId = wallet.Id, Amount = 50000, Currency = "VND", Purpose = "Deposit", ReferenceType = "Wallet", ReferenceId = wallet.Id.ToString(), Provider = "payos", ProviderOrderCode = 123456, IdempotencyKey = "audit-payment", ExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        f.Db.Payments.Add(payment); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Wallet.CompleteDepositAsync(123456, "provider-id", 60000, default));
        await f.Wallet.CompleteDepositAsync(123456, "provider-id", 50000, default);
        await f.Wallet.CompleteDepositAsync(123456, "provider-id", 50000, default);
        Assert.Equal(150000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Single(await f.Db.WalletTransactions.ToListAsync());
    }
    [Fact] public async Task CaptureDoesNotAppearAsSecondWithdrawalInUserHistory()
    {
        await using var f = await Fixture.Create();
        var w = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        await f.Service.ChangeStatusAsync(w.Id, WithdrawalStatus.Processing, null, default);
        await f.Service.ChangeStatusAsync(w.Id, WithdrawalStatus.Completed, null, default);
        var history = await f.Wallet.GetTransactionsAsync(f.UserId, 1, 20, null, null, default);
        Assert.Single(history.Data);
        Assert.Equal(WalletTransactionType.Withdrawal, history.Data[0].Type);
    }
    [Fact] public async Task CorruptEncryptedAccountCannotProduceTransferQr()
    {
        await using var f = await Fixture.Create();
        var w = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        (await f.Db.BankAccounts.SingleAsync()).AccountNumberEncrypted = "invalid";
        await f.Db.SaveChangesAsync();
        var detail = await f.Service.GetAdminDetailAsync(w.Id, default);
        Assert.Null(detail!.AccountNumber); Assert.Null(detail.QrImageUrl);
        Assert.False(detail.CanTransfer);
        await Assert.ThrowsAsync<WalletValidationException>(() => f.Service.ChangeStatusAsync(w.Id, WithdrawalStatus.Processing, null, default));
        Assert.Equal(45000m, detail.Withdrawal.NetAmount);
    }
    [Fact] public async Task InconsistentReservationCannotBeSettled()
    {
        await using var f = await Fixture.Create();
        var w = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        (await f.Db.WalletTransactions.SingleAsync()).ReferenceId = Guid.NewGuid().ToString("D");
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Service.ChangeStatusAsync(w.Id, WithdrawalStatus.Rejected, "Review", default));
        Assert.Equal(50000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Equal(50000m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
    }
    [Fact] public async Task GiftMovesExactVndOnceAndPreservesHeldAndCoin()
    {
        await using var f = await Fixture.Create();
        var account = new Account { Email = "host@example.test" };
        var host = new User { AccountId = account.Id, DisplayName = "Host" };
        var category = new LiveCategory { Name = "Chat", Slug = "audit-chat" };
        var live = new Viora.Domain.Entities.Live { HostUserId = host.Id, CategoryId = category.Id, Title = "Audit live", AgoraChannelName = "audit-live", Status = LiveStatus.Live };
        var gift = new LiveGift { Name = "Rose", ImageUrl = "/rose.png", Price = 10000, PriceCoin = 1 };
        var sender = await f.Db.Wallets.SingleAsync(); sender.HeldBalance = 2000; sender.AnktCoinBalance = 123;
        f.Db.AddRange(account, host, category, live, gift, new Wallet { UserId = host.Id, AvailableBalance = 5000, HeldBalance = 1000, AnktCoinBalance = 456 });
        await f.Db.SaveChangesAsync();
        var service = new Viora.Infrastructure.LiveStreaming.LiveGiftWalletService(f.Db);
        var requestId = Guid.NewGuid();
        var first = await service.SendAsync(f.UserId, live.Id, gift.Id, 3, requestId, default, 10000);
        var replay = await service.SendAsync(f.UserId, live.Id, gift.Id, 3, requestId, default, 10000);
        Assert.True(replay.IsReplay); Assert.Equal(first.Transaction.Id, replay.Transaction.Id);
        Assert.Equal(70000m, first.SenderBalance); Assert.Equal(35000m, first.ReceiverBalance);
        Assert.Equal(2, await f.Db.WalletTransactions.CountAsync()); Assert.Single(await f.Db.LiveGiftTransactions.ToListAsync());
        var wallets = await f.Db.Wallets.ToListAsync(); Assert.Equal(105000m, wallets.Sum(w => w.AvailableBalance));
        Assert.Equal(3000m, wallets.Sum(w => w.HeldBalance)); Assert.Equal(579L, wallets.Sum(w => w.AnktCoinBalance));
        await Assert.ThrowsAsync<WalletConflictException>(() => service.SendAsync(f.UserId, live.Id, gift.Id, 4, requestId, default, 10000));
    }
    [Fact] public async Task AdminArticleDetailIncludesOrderedContentBlocks()
    {
        await using var f = await Fixture.Create();
        var post = new Post { UserId = f.UserId, PostType = PostType.Article, Content = "Article title" };
        f.Db.AddRange(post,
            new ArticleBlock { PostId = post.Id, OrderIndex = 1, BlockType = ArticleBlockType.Text, Content = "Article body" },
            new ArticleBlock { PostId = post.Id, OrderIndex = 0, BlockType = ArticleBlockType.Heading, Content = "Heading" });
        await f.Db.SaveChangesAsync();
        var repository = new Viora.Infrastructure.Persistence.Repositories.AdminRepository(f.Db);
        var detail = await repository.GetPostDetailAsync(post.Id, null, default);
        Assert.Equal(new[] { "Heading", "Article body" }, detail!.ArticleBlocks!.Select(block => block.Content).ToArray());
    }
    [Fact] public async Task PercentageFeeIsPersistentAndExistingWithdrawalsKeepTheirFee()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Service.GetFeeSettingsAsync(default);
        Assert.Equal(10m, initial.FeePercent);
        var quote = await f.Service.QuoteAsync(60000, default);
        Assert.Equal(6000m, quote.Fee);
        var request = f.Request(60000) with { ExpectedFee = quote.Fee };
        var withdrawal = await f.Service.CreateAsync(f.UserId, request, default);
        var updated = await f.Service.UpdateFeeSettingsAsync(f.UserId, 12.5m, initial.Version, default);
        Assert.Equal(12.5m, (await f.Service.GetFeeSettingsAsync(default)).FeePercent);
        Assert.Equal(7500m, (await f.Service.QuoteAsync(60000, default)).Fee);
        var replay = await f.Service.CreateAsync(f.UserId, request, default);
        Assert.Equal(withdrawal.Id, replay.Id);
        Assert.Equal(6000m, replay.Fee);
        Assert.Equal(10m, replay.FeePercent);
        Assert.Equal(54000m, replay.NetAmount);
        Assert.Equal(initial.Version + 1, updated.Version);
        Assert.Equal(f.UserId, updated.UpdatedBy);
        Assert.Single(await f.Db.AdminLogs.ToListAsync());
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Service.UpdateFeeSettingsAsync(f.UserId, 20, initial.Version, default));
        await f.Service.ChangeStatusAsync(withdrawal.Id, WithdrawalStatus.Cancelled, null, default);
        Assert.Equal(100000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
    }
    [Fact] public async Task ChangedQuoteDoesNotReserveAndRequiresNewConfirmation()
    {
        await using var f = await Fixture.Create();
        var quote = await f.Service.QuoteAsync(50000, default);
        var initial = await f.Service.GetFeeSettingsAsync(default);
        await f.Service.UpdateFeeSettingsAsync(f.UserId, 20, initial.Version, default);
        await Assert.ThrowsAsync<WalletConflictException>(() => f.Service.CreateAsync(f.UserId, f.Request(50000) with { ExpectedFee = quote.Fee }, default));
        Assert.Empty(await f.Db.Withdrawals.ToListAsync());
        Assert.Equal(100000m, (await f.Db.Wallets.SingleAsync()).AvailableBalance);
        Assert.Equal(0m, (await f.Db.Wallets.SingleAsync()).HeldBalance);
        var fresh = await f.Service.QuoteAsync(50000, default);
        Assert.Equal(40000m, (await f.Service.CreateAsync(f.UserId, f.Request(50000) with { ExpectedFee = fresh.Fee }, default)).NetAmount);
    }
    [Fact] public async Task FeeRatesValidateAndRoundToWholeVnd()
    {
        await using var f = await Fixture.Create();
        var settings = await f.Service.GetFeeSettingsAsync(default);
        foreach (var invalid in new[] { -1m, 100m, 0.001m, 99.999m })
            await Assert.ThrowsAsync<WalletValidationException>(() => f.Service.UpdateFeeSettingsAsync(f.UserId, invalid, settings.Version, default));
        settings = await f.Service.UpdateFeeSettingsAsync(f.UserId, 0, settings.Version, default);
        Assert.Equal(50001m, (await f.Service.QuoteAsync(50001, default)).NetAmount);
        settings = await f.Service.UpdateFeeSettingsAsync(f.UserId, 12.5m, settings.Version, default);
        Assert.Equal(6251m, (await f.Service.QuoteAsync(50004, default)).Fee);
        await f.Service.UpdateFeeSettingsAsync(f.UserId, 99.99m, settings.Version, default);
        Assert.Equal(5m, (await f.Service.QuoteAsync(50000, default)).NetAmount);
    }
    [Fact] public async Task LegacyFixedFeeRemainsUnchangedAndQrUsesSavedNet()
    {
        await using var f = await Fixture.Create();
        var withdrawal = await f.Service.CreateAsync(f.UserId, f.Request(50000), default);
        var legacy = await f.Db.Withdrawals.SingleAsync();
        legacy.Fee = 3000; legacy.NetAmount = 47000; legacy.FeePercent = null;
        await f.Db.SaveChangesAsync();
        var settings = await f.Service.GetFeeSettingsAsync(default);
        await f.Service.UpdateFeeSettingsAsync(f.UserId, 25, settings.Version, default);
        await f.Service.ChangeStatusAsync(withdrawal.Id, WithdrawalStatus.Processing, null, default, f.UserId);
        var detail = await f.Service.GetAdminDetailAsync(withdrawal.Id, default);
        Assert.Null(detail!.Withdrawal.FeePercent);
        Assert.Equal(3000m, detail.Withdrawal.Fee);
        Assert.Contains("amount=47000", detail.QrImageUrl!);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required AppDbContext Db { get; init; }
        public required WalletService Wallet { get; init; }
        public required WithdrawalService Service { get; init; }
        public Guid UserId { get; init; }
        public Guid BankId { get; set; }
        public CreateWithdrawalRequest Request(decimal amount) => new(amount, BankId, Guid.NewGuid().ToString("N"));
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction("char_length", (string value) => value.Length);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var account = new Account { Email = "audit@example.test" };
            var user = new User { AccountId = account.Id, DisplayName = "Audit user" };
            db.AddRange(account, user, new Wallet { UserId = user.Id, AvailableBalance = 100000 });
            await db.SaveChangesAsync();
            var options = Options.Create(new WithdrawalOptions { BankAccountEncryptionKey = Convert.ToBase64String(new byte[32]) });
            var wallet = new WalletService(db);
            var service = new WithdrawalService(db, wallet, new BankAccountProtector(options), options);
            var bank = await service.CreateBankAccountAsync(user.Id, new("VCB", "ignored", "1234567890", "AUDIT USER", true), default);
            return new() { Connection = connection, Db = db, Wallet = wallet, Service = service, UserId = user.Id, BankId = bank.Id };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
