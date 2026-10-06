using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.LiveStreaming;
using Viora.Infrastructure.Realtime;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class LiveGiftPaymentApiTests
{
    [Fact]
    public async Task Price_change_after_confirmation_requires_new_confirmation_without_payment()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        (await scope.Db.LiveGifts.SingleAsync()).Price = 20_000;
        await scope.Db.SaveChangesAsync();
        var hub = new RecordingHub();
        var response = Assert.IsType<ConflictObjectResult>(await Controller(scope, hub).Send(scope.LiveId, new(scope.GiftId, 1, Guid.NewGuid()), default));
        Assert.Equal("LIVE_GIFT_PRICE_CHANGED", Json(response.Value).GetProperty("code").GetString());
        await scope.AssertUnchangedAsync();
        Assert.Equal(0, hub.Calls);
    }

    [Fact]
    public async Task Admin_cannot_create_whitespace_gift_name()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var controller = new LiveGiftsController(scope.Db);
        Assert.IsType<UnprocessableEntityObjectResult>(await controller.Create(
            new("  ", "https://example.com/rose.png", null, 10_000, LiveGiftAnimationType.Small, 0, true), default));
        Assert.Equal(1, await scope.Db.LiveGifts.CountAsync());
    }

    [Fact]
    public async Task Legacy_coin_client_cannot_pay_from_VND_wallet()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var hub = new RecordingHub();
        var controller = Controller(scope, hub, currency: null);
        Assert.IsType<ConflictObjectResult>(await controller.Send(scope.LiveId, new(scope.GiftId, 1, Guid.NewGuid()), default));
        await scope.AssertUnchangedAsync();
        Assert.Equal(0, hub.Calls);
    }

    [Fact]
    public async Task Gift_event_observes_committed_transfer_and_replay_does_not_broadcast_twice()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var hub = new RecordingHub(async () =>
        {
            Assert.Null(scope.Db.Database.CurrentTransaction);
            Assert.Equal(70_000m, await scope.SenderBalanceAsync());
            Assert.Equal(35_000m, await scope.ReceiverBalanceAsync());
            await scope.AssertCountsAsync(1, 2);
        });
        var controller = Controller(scope, hub);
        var request = new SendLiveGiftBody(scope.GiftId, 3, Guid.NewGuid());
        var result = Json(Assert.IsType<OkObjectResult>(await controller.Send(scope.LiveId, request, default)).Value);
        Assert.Equal(30_000, result.GetProperty("totalAmount").GetInt64());
        Assert.Equal(70_000m, result.GetProperty("senderBalance").GetDecimal());
        Assert.False(result.TryGetProperty("receiverBalance", out _));
        Assert.IsType<OkObjectResult>(await controller.Send(scope.LiveId, request, default));
        Assert.Equal(1, hub.Calls);
        Assert.Equal("LiveGift", hub.Method);
        var payload = Json(hub.Payload);
        Assert.Equal(3, payload.GetProperty("quantity").GetInt32());
        Assert.Equal(30_000, payload.GetProperty("totalAmount").GetInt64());
        Assert.Equal(result.GetProperty("transactionId").GetGuid(), payload.GetProperty("transactionId").GetGuid());
    }

    [Fact]
    public async Task Insufficient_balance_has_no_animation_event()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var hub = new RecordingHub();
        var failure = Assert.IsType<ConflictObjectResult>(await Controller(scope, hub).Send(scope.LiveId, new(scope.GiftId, 11, Guid.NewGuid()), default));
        Assert.Equal("INSUFFICIENT_BALANCE", Json(failure.Value).GetProperty("code").GetString());
        Assert.Equal(0, hub.Calls);
        await scope.AssertUnchangedAsync();
    }

    [Fact]
    public async Task Realtime_failure_returns_paid_result_and_retry_does_not_charge_again()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var hub = new RecordingHub(() => throw new InvalidOperationException("Injected realtime failure"));
        var controller = Controller(scope, hub);
        var request = new SendLiveGiftBody(scope.GiftId, 1, Guid.NewGuid());
        Assert.IsType<OkObjectResult>(await controller.Send(scope.LiveId, request, default));
        Assert.IsType<OkObjectResult>(await controller.Send(scope.LiveId, request, default));
        Assert.Equal(90_000m, await scope.SenderBalanceAsync());
        Assert.Equal(15_000m, await scope.ReceiverBalanceAsync());
        await scope.AssertCountsAsync(1, 2);
        Assert.Equal(1, hub.Calls);
    }

    [Fact]
    public async Task Income_and_top_gifters_exclude_coin_history_and_survive_live_ending()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        await scope.SendAsync(3);
        var senderWallet = await scope.Db.Wallets.SingleAsync(x => x.UserId == scope.SenderId);
        var coinLedger = new WalletTransaction
        {
            WalletId = senderWallet.Id, Type = WalletTransactionType.Payment, Amount = 1,
            ReferenceType = "LiveGiftCoin", ReferenceId = Guid.NewGuid().ToString(),
            IdempotencyKey = Guid.NewGuid().ToString(), Status = WalletTransactionStatus.Completed
        };
        scope.Db.AddRange(coinLedger, new LiveGiftTransaction
        {
            RequestId = Guid.NewGuid(), LiveId = scope.LiveId, GiftId = scope.GiftId,
            SenderUserId = scope.SenderId, HostUserId = scope.ReceiverId,
            WalletTransactionId = coinLedger.Id, Quantity = 99, UnitPriceCoin = 1, TotalCoin = 99
        });
        (await scope.Db.Lives.SingleAsync()).Status = LiveStatus.Ended;
        await scope.Db.SaveChangesAsync();
        // SQLite stores decimal zero as TEXT '0.0'; use the canonical TEXT '0' for the legacy PostgreSQL CHECK.
        var sqliteZero = "0";
        await scope.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WalletTransactions\" SET \"Amount\" = {sqliteZero}, \"CoinAmount\" = -1, \"CoinBalanceBefore\" = 123, \"CoinBalanceAfter\" = 122 WHERE \"Id\" = {coinLedger.Id}");
        var host = Controller(scope, new RecordingHub(), scope.ReceiverId);
        var income = Json(Assert.IsType<OkObjectResult>(await host.Income(scope.LiveId, default)).Value);
        Assert.Equal(3, income.GetProperty("totalGiftCount").GetInt64());
        Assert.Equal(30_000, income.GetProperty("totalGiftValue").GetInt64());
        Assert.Equal(30_000, income.GetProperty("netAmount").GetInt64());
        Assert.Equal(1, income.GetProperty("senderCount").GetInt32());
        var top = Json(Assert.IsType<OkObjectResult>(await host.TopGifters(scope.LiveId, default)).Value);
        Assert.Equal(1, top.GetArrayLength());
        Assert.Equal(30_000, top[0].GetProperty("totalAmount").GetInt64());
        Assert.IsType<OkObjectResult>(await host.History(scope.LiveId, default));
        Assert.IsType<ForbidResult>(await Controller(scope, new RecordingHub()).Income(scope.LiveId, default));
    }

    [Fact]
    public async Task Both_wallet_histories_show_gift_and_counterparty_with_correct_amounts()
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        await scope.SendAsync(3);
        var wallets = new Viora.Infrastructure.Wallets.WalletService(scope.Db);
        var sent = Assert.Single((await wallets.GetTransactionsAsync(scope.SenderId, 1, 20, null, WalletHistoryGroup.Payment, default)).Data);
        var received = Assert.Single((await wallets.GetTransactionsAsync(scope.ReceiverId, 1, 20, null, null, default)).Data);
        Assert.Equal(-30_000m, sent.Amount);
        Assert.Equal(30_000m, received.Amount);
        Assert.Equal("Streamer", sent.Destination);
        Assert.Equal("Sender", received.Source);
        Assert.Equal("Rose ×3", sent.RelatedContent);
        Assert.Equal("Rose ×3", received.RelatedContent);
    }

    private static LiveGiftTransactionsController Controller(LiveGiftWalletIntegrationTests.GiftScope scope,
        RecordingHub hub, Guid? userId = null, string? currency = "VND")
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", (userId ?? scope.SenderId).ToString())], "test"))
        };
        if (currency is not null) http.Request.Headers["X-Live-Gift-Currency"] = currency;
        http.Request.Headers["X-Live-Gift-Expected-Price"] = "10000";
        return new(scope.Db, hub, new LiveGiftWalletService(scope.Db), NullLogger<LiveGiftTransactionsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private sealed class RecordingHub(Func<Task>? beforeSend = null) : IHubContext<RealtimeHub>, IHubClients, IClientProxy
    {
        public int Calls { get; private set; }
        public string? Method { get; private set; }
        public object? Payload { get; private set; }
        public IHubClients Clients => this;
        public IGroupManager Groups => null!;
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
        public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Calls++;
            Method = method;
            Payload = args.Single();
            if (beforeSend is not null) await beforeSend();
        }
    }
}
