using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Wallets;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.LiveStreaming;

public sealed record LiveGiftPaymentResult(LiveGiftTransaction Transaction, decimal SenderBalance,
    decimal ReceiverBalance, bool IsReplay, LiveGift Gift, string SenderName, string? SenderAvatarUrl);

public sealed class LiveGiftWalletService(AppDbContext db)
{
    public async Task<LiveGiftPaymentResult> SendAsync(Guid userId, Guid liveId, Guid giftId,
        int quantity, Guid requestId, CancellationToken cancellationToken, long? expectedUnitPrice = null)
    {
        if (requestId == Guid.Empty || giftId == Guid.Empty || quantity is < 1 or > 99)
            throw new WalletValidationException("LIVE_GIFT_INVALID", "Yêu cầu quà không hợp lệ.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (db.Database.IsNpgsql())
            {
                // Serialize the request key even when a replay changes sender/live.
                var lockKey = BitConverter.ToInt64(requestId.ToByteArray(), 0);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
            }
            var existing = await db.LiveGiftTransactions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.SenderUserId != userId || existing.LiveId != liveId || existing.GiftId != giftId || existing.Quantity != quantity)
                    throw new WalletConflictException("GIFT_REQUEST_REUSED", "Mã yêu cầu đã được sử dụng cho giao dịch khác.");
                if (expectedUnitPrice is not null && existing.UnitPrice != expectedUnitPrice)
                    throw new WalletConflictException("GIFT_REQUEST_REUSED", "Mã yêu cầu đã được sử dụng với giá quà khác.");
                var replay = await ResultAsync(existing, true, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            // Lock live before wallets so ending the room cannot race settlement.
            var liveQuery = db.Database.IsNpgsql()
                ? db.Lives.FromSqlInterpolated($"SELECT * FROM \"Lives\" WHERE \"Id\" = {liveId} FOR UPDATE")
                : db.Lives.Where(x => x.Id == liveId);
            var live = (await liveQuery.AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
            if (live is null || live.Status != LiveStatus.Live || !live.AllowGifts)
                throw new WalletConflictException("LIVE_GIFT_UNAVAILABLE", "Live không nhận quà lúc này.");
            if (live.HostUserId == userId)
                throw new WalletConflictException("LIVE_SELF_GIFT", "Không thể tặng quà cho chính mình.");
            var giftQuery = db.Database.IsNpgsql()
                ? db.LiveGifts.FromSqlInterpolated($"SELECT * FROM \"LiveGifts\" WHERE \"Id\" = {giftId} FOR SHARE")
                : db.LiveGifts.Where(x => x.Id == giftId);
            var gift = (await giftQuery.AsNoTracking().ToListAsync(cancellationToken)).SingleOrDefault();
            if (gift is null || !gift.IsActive || string.IsNullOrWhiteSpace(gift.Name))
                throw new WalletConflictException("LIVE_GIFT_UNAVAILABLE", "Quà hiện không khả dụng.");
            if (gift.Price <= 0 || gift.Price > 9_007_199_254_740_991L / quantity)
                throw new WalletValidationException("LIVE_GIFT_INVALID", "Giá quà không hợp lệ.");
            if (expectedUnitPrice is not null && gift.Price != expectedUnitPrice)
                throw new WalletConflictException("LIVE_GIFT_PRICE_CHANGED", "Giá quà đã thay đổi. Vui lòng chọn lại quà và xác nhận giá mới.");
            var total = checked(gift.Price * quantity);

            if (!await db.Users.AnyAsync(x => x.Id == userId && x.Account.Status == AccountStatus.Active, cancellationToken) ||
                !await db.Users.AnyAsync(x => x.Id == live.HostUserId && x.Account.Status == AccountStatus.Active, cancellationToken) ||
                await db.LiveUserRestrictions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId && x.IsBlocked, cancellationToken) ||
                live.Privacy == LivePrivacy.Followers && !await db.Follows.AnyAsync(x => x.FollowerId == userId && x.FollowingId == live.HostUserId, cancellationToken) ||
                live.Privacy == LivePrivacy.Friends && !await db.Friendships.AnyAsync(x => x.Status == FriendshipStatus.Accepted &&
                    ((x.RequesterUserId == userId && x.AddresseeUserId == live.HostUserId) ||
                     (x.AddresseeUserId == userId && x.RequesterUserId == live.HostUserId)), cancellationToken))
                throw new WalletConflictException("LIVE_GIFT_FORBIDDEN", "Bạn không có quyền tặng quà trong Live này.");

            // All gift transfers acquire both wallet rows in the same database order.
            var walletQuery = db.Database.IsNpgsql()
                ? db.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"UserId\" IN ({userId}, {live.HostUserId}) ORDER BY \"Id\" FOR UPDATE")
                : db.Wallets.Where(x => x.UserId == userId || x.UserId == live.HostUserId).OrderBy(x => x.Id);
            var wallets = await walletQuery.ToListAsync(cancellationToken);
            foreach (var wallet in wallets) await db.Entry(wallet).ReloadAsync(cancellationToken);
            var sender = wallets.SingleOrDefault(x => x.UserId == userId);
            var receiver = wallets.SingleOrDefault(x => x.UserId == live.HostUserId);
            if (sender is null || receiver is null || sender.Status != WalletStatus.Active || receiver.Status != WalletStatus.Active ||
                sender.Currency != "VND" || receiver.Currency != "VND")
                throw new WalletConflictException("WALLET_UNAVAILABLE", "Ví người gửi hoặc chủ Live không khả dụng.");
            WalletFinancialRules.RequireSufficientBalance(sender.AvailableBalance, total);

            var names = await db.Users.AsNoTracking().Where(x => x.Id == userId || x.Id == live.HostUserId)
                .Select(x => new { x.Id, x.DisplayName, x.AvatarUrl }).ToListAsync(cancellationToken);
            var senderUser = names.Single(x => x.Id == userId);
            var receiverUser = names.Single(x => x.Id == live.HostUserId);
            var payment = new LiveGiftTransaction
            {
                RequestId = requestId, LiveId = liveId, GiftId = gift.Id, SenderUserId = userId, HostUserId = live.HostUserId,
                Quantity = quantity, UnitPrice = gift.Price, GrossAmount = total, FeeAmount = 0, NetAmount = total,
                Currency = "VND", GiftName = gift.Name, Status = WalletTransactionStatus.Completed
            };
            var metadata = JsonSerializer.Serialize(new
            {
                liveId, giftId, giftName = gift.Name, giftPrice = gift.Price, quantity,
                senderUserId = userId, receiverUserId = live.HostUserId, giftTransactionId = payment.Id,
                senderName = senderUser.DisplayName, receiverName = receiverUser.DisplayName
            });
            var sent = Ledger(sender, payment, -total, WalletTransactionType.LiveGiftSent,
                $"Tặng {gift.Name} ×{quantity} cho {receiverUser.DisplayName}", metadata, "sent");
            var received = Ledger(receiver, payment, total, WalletTransactionType.LiveGiftReceived,
                $"Nhận {gift.Name} ×{quantity} từ {senderUser.DisplayName}", metadata, "received");
            sender.AvailableBalance = sent.BalanceAfter;
            receiver.AvailableBalance = received.BalanceAfter;
            payment.WalletTransactionId = sent.Id;
            payment.ReceiverWalletTransactionId = received.Id;
            db.WalletTransactions.AddRange(sent, received);
            db.LiveGiftTransactions.Add(payment);
            await db.SaveChangesAsync(cancellationToken);
            // Legacy live counters retain Coin history; VND statistics are derived from VND transactions.
            await transaction.CommitAsync(cancellationToken);
            return new(payment, sender.AvailableBalance, receiver.AvailableBalance, false, gift, senderUser.DisplayName, senderUser.AvatarUrl);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<LiveGiftPaymentResult> ResultAsync(LiveGiftTransaction payment, bool replay, CancellationToken cancellationToken)
    {
        var gift = await db.LiveGifts.AsNoTracking().SingleAsync(x => x.Id == payment.GiftId, cancellationToken);
        var balances = await db.Wallets.AsNoTracking().Where(x => x.UserId == payment.SenderUserId || x.UserId == payment.HostUserId)
            .Select(x => new { x.UserId, x.AvailableBalance }).ToListAsync(cancellationToken);
        var sender = await db.Users.AsNoTracking().Where(x => x.Id == payment.SenderUserId)
            .Select(x => new { x.DisplayName, x.AvatarUrl }).SingleAsync(cancellationToken);
        return new(payment, balances.Single(x => x.UserId == payment.SenderUserId).AvailableBalance,
            balances.Single(x => x.UserId == payment.HostUserId).AvailableBalance, replay, gift, sender.DisplayName, sender.AvatarUrl);
    }

    private static WalletTransaction Ledger(Wallet wallet, LiveGiftTransaction payment, decimal amount,
        WalletTransactionType type, string description, string metadata, string direction) => new()
    {
        WalletId = wallet.Id, Type = type, Amount = amount, BalanceBefore = wallet.AvailableBalance,
        BalanceAfter = checked(wallet.AvailableBalance + amount), HeldBefore = wallet.HeldBalance, HeldAfter = wallet.HeldBalance,
        ReferenceType = "LiveGift", ReferenceId = payment.Id.ToString("D"), Description = description, Metadata = metadata,
        Status = WalletTransactionStatus.Completed, IdempotencyKey = $"live-gift:{payment.RequestId:N}:{direction}", CompletedAt = DateTime.UtcNow
    };
}
