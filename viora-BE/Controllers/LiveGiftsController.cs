using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Realtime;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/live-gifts")]
public sealed class LiveGiftsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Active(CancellationToken cancellationToken) => Ok(await db.LiveGifts.AsNoTracking()
        .Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
        .Select(x => new LiveGiftDto(x.Id, x.Name, x.ImageUrl, x.AnimationUrl, x.PriceCoin, x.AnimationType, x.SortOrder, x.IsActive, db.LiveGiftTransactions.Count(g => g.GiftId == x.Id), x.EffectType, x.EffectTier, x.EffectDurationMs))
        .ToListAsync(cancellationToken));

    [Authorize(Roles = "2")]
    [HttpGet("admin")]
    public async Task<IActionResult> All(CancellationToken cancellationToken) => Ok(await db.LiveGifts.AsNoTracking()
        .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
        .Select(x => new LiveGiftDto(x.Id, x.Name, x.ImageUrl, x.AnimationUrl, x.PriceCoin, x.AnimationType, x.SortOrder, x.IsActive, db.LiveGiftTransactions.Count(g => g.GiftId == x.Id), x.EffectType, x.EffectTier, x.EffectDurationMs))
        .ToListAsync(cancellationToken));

    [Authorize(Roles = "2")]
    [HttpPost]
    public async Task<IActionResult> Create(LiveGiftBody body, CancellationToken cancellationToken)
    {
        var effectType = body.EffectType ?? LiveGiftEffectType.None;
        var effectTier = body.EffectTier ?? 0;
        var effectDurationMs = body.EffectDurationMs ?? 0;
        if (!Valid(body, effectType, effectTier, effectDurationMs)) return UnprocessableEntity(new { code = "LIVE_GIFT_INVALID" });
        var gift = new LiveGift { Name = body.Name.Trim(), ImageUrl = body.ImageUrl.Trim(), AnimationUrl = body.AnimationUrl?.Trim(), PriceCoin = body.PriceCoin, AnimationType = body.AnimationType, SortOrder = body.SortOrder, IsActive = body.IsActive, EffectType = effectType, EffectTier = effectTier, EffectDurationMs = effectDurationMs };
        db.LiveGifts.Add(gift);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/live-gifts/{gift.Id}", Map(gift, 0));
    }

    [Authorize(Roles = "2")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, LiveGiftBody body, CancellationToken cancellationToken)
    {
        var gift = await db.LiveGifts.FindAsync([id], cancellationToken);
        if (gift is null) return NotFound();
        var effectType = body.EffectType ?? gift.EffectType;
        var effectTier = body.EffectTier ?? (body.EffectType == LiveGiftEffectType.None ? (short)0 : gift.EffectTier);
        var effectDurationMs = body.EffectDurationMs ?? (body.EffectType == LiveGiftEffectType.None ? 0 : gift.EffectDurationMs);
        if (!Valid(body, effectType, effectTier, effectDurationMs)) return UnprocessableEntity(new { code = "LIVE_GIFT_INVALID" });
        gift.Name = body.Name.Trim(); gift.ImageUrl = body.ImageUrl.Trim(); gift.AnimationUrl = body.AnimationUrl?.Trim();
        gift.PriceCoin = body.PriceCoin; gift.AnimationType = body.AnimationType; gift.SortOrder = body.SortOrder; gift.IsActive = body.IsActive;
        gift.EffectType = effectType; gift.EffectTier = effectTier; gift.EffectDurationMs = effectDurationMs;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Map(gift, await db.LiveGiftTransactions.CountAsync(x => x.GiftId == id, cancellationToken)));
    }

    [Authorize(Roles = "2")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var gift = await db.LiveGifts.FindAsync([id], cancellationToken);
        if (gift is null) return NotFound();
        if (await db.LiveGiftTransactions.AnyAsync(x => x.GiftId == id, cancellationToken)) return Conflict(new { code = "LIVE_GIFT_IN_USE" });
        db.LiveGifts.Remove(gift);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static bool Valid(LiveGiftBody body, LiveGiftEffectType effectType, short effectTier, int effectDurationMs) => body.Name is { Length: > 0 and <= 100 } &&
        body.PriceCoin > 0 && Enum.IsDefined(body.AnimationType) && Enum.IsDefined(effectType) &&
        (effectType == LiveGiftEffectType.None
            ? effectTier == 0 && effectDurationMs == 0
            : effectTier is >= 1 and <= 3 && effectDurationMs is >= 3000 and <= 7000) &&
        Uri.TryCreate(body.ImageUrl, UriKind.Absolute, out var image) && image.Scheme == Uri.UriSchemeHttps &&
        (body.AnimationUrl is null || Uri.TryCreate(body.AnimationUrl, UriKind.Absolute, out var animation) && animation.Scheme == Uri.UriSchemeHttps);
    private static LiveGiftDto Map(LiveGift gift, int useCount) => new(gift.Id, gift.Name, gift.ImageUrl, gift.AnimationUrl, gift.PriceCoin, gift.AnimationType, gift.SortOrder, gift.IsActive, useCount, gift.EffectType, gift.EffectTier, gift.EffectDurationMs);
}

public sealed record LiveGiftBody(string Name, string ImageUrl, string? AnimationUrl, long PriceCoin, LiveGiftAnimationType AnimationType, int SortOrder, bool IsActive, LiveGiftEffectType? EffectType = null, short? EffectTier = null, int? EffectDurationMs = null);
public sealed record LiveGiftDto(Guid Id, string Name, string ImageUrl, string? AnimationUrl, long PriceCoin, LiveGiftAnimationType AnimationType, int SortOrder, bool IsActive, int UseCount, LiveGiftEffectType EffectType, short EffectTier, int EffectDurationMs);

[ApiController]
[Route("api/lives/{liveId:guid}/gifts")]
public sealed class LiveGiftTransactionsController(AppDbContext db, IHubContext<RealtimeHub> realtime, IConfiguration configuration) : ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Send(Guid liveId, SendLiveGiftBody body, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return Unauthorized();
        if (body.RequestId == Guid.Empty || body.GiftId == Guid.Empty || body.Quantity is < 1 or > 99)
            return UnprocessableEntity(new { code = "LIVE_GIFT_INVALID" });
        var feePercent = configuration.GetValue<decimal?>("Live:GiftPlatformFeePercent");
        if (feePercent is null or < 0 or > 100) return StatusCode(503, new { code = "LIVE_GIFT_FEE_INVALID" });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // A row lock serializes all Coin spending from this wallet, including concurrent gifts.
        var wallet = (await db.Wallets.FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"UserId\" = {userId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (wallet is null || wallet.Status != WalletStatus.Active) return Conflict(new { code = "WALLET_UNAVAILABLE" });
        var existing = await db.LiveGiftTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == body.RequestId, cancellationToken);
        if (existing is not null)
            return existing.SenderUserId == userId && existing.LiveId == liveId && existing.GiftId == body.GiftId && existing.Quantity == body.Quantity
                ? Ok(Map(existing)) : Conflict(new { code = "GIFT_REQUEST_REUSED" });

        var live = await db.Lives.AsNoTracking().SingleOrDefaultAsync(x => x.Id == liveId, cancellationToken);
        var gift = await db.LiveGifts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == body.GiftId && x.IsActive, cancellationToken);
        if (live is null || live.Status != LiveStatus.Live || !live.AllowGifts || gift is null)
            return Conflict(new { code = "LIVE_GIFT_UNAVAILABLE" });
        if (live.HostUserId == userId) return Conflict(new { code = "LIVE_SELF_GIFT" });
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.Account.Status == AccountStatus.Active, cancellationToken) ||
            await db.LiveUserRestrictions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId && x.IsBlocked, cancellationToken) ||
            live.Privacy == LivePrivacy.Followers && !await db.Follows.AnyAsync(x => x.FollowerId == userId && x.FollowingId == live.HostUserId, cancellationToken) ||
            live.Privacy == LivePrivacy.Friends && !await db.Friendships.AnyAsync(x => x.Status == FriendshipStatus.Accepted &&
                ((x.RequesterUserId == userId && x.AddresseeUserId == live.HostUserId) || (x.AddresseeUserId == userId && x.RequesterUserId == live.HostUserId)), cancellationToken))
            return Forbid();
        long total;
        try { total = checked(gift.PriceCoin * body.Quantity); }
        catch (OverflowException) { return UnprocessableEntity(new { code = "LIVE_GIFT_INVALID" }); }
        if (wallet.AnktCoinBalance < total) return Conflict(new { code = "INSUFFICIENT_COIN", balance = wallet.AnktCoinBalance });
        var platformFee = (long)Math.Round(total * feePercent.Value / 100m, 0, MidpointRounding.AwayFromZero);
        var now = DateTime.UtcNow;
        var giftTransaction = new LiveGiftTransaction
        {
            RequestId = body.RequestId, LiveId = liveId, GiftId = gift.Id, SenderUserId = userId, HostUserId = live.HostUserId,
            Quantity = body.Quantity, UnitPriceCoin = gift.PriceCoin, TotalCoin = total, FeePercent = feePercent.Value,
            PlatformFee = platformFee, HostEarning = total - platformFee
        };
        var ledger = new WalletTransaction
        {
            WalletId = wallet.Id, Type = WalletTransactionType.Payment, Amount = 0,
            BalanceBefore = wallet.AvailableBalance, BalanceAfter = wallet.AvailableBalance,
            HeldBefore = wallet.HeldBalance, HeldAfter = wallet.HeldBalance,
            CoinAmount = -total, CoinBalanceBefore = wallet.AnktCoinBalance, CoinBalanceAfter = wallet.AnktCoinBalance - total,
            ReferenceType = "LiveGiftCoin", ReferenceId = giftTransaction.Id.ToString("D"),
            Description = $"Live gift: {gift.Name} x{body.Quantity}", Status = WalletTransactionStatus.Completed,
            IdempotencyKey = $"live-gift:{body.RequestId:N}", CompletedAt = now
        };
        wallet.AnktCoinBalance -= total;
        giftTransaction.WalletTransactionId = ledger.Id;
        db.WalletTransactions.Add(ledger);
        db.LiveGiftTransactions.Add(giftTransaction);
        await db.SaveChangesAsync(cancellationToken);
        await db.Lives.Where(x => x.Id == liveId).ExecuteUpdateAsync(x => x
            .SetProperty(l => l.TotalGiftCount, l => l.TotalGiftCount + body.Quantity)
            .SetProperty(l => l.TotalGiftValue, l => l.TotalGiftValue + total), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var sender = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.DisplayName, x.AvatarUrl }).SingleAsync(cancellationToken);
        await realtime.Clients.Group($"live:{liveId:N}").SendAsync("LiveGift", new
        {
            giftTransaction.Id, liveId, senderUserId = userId, senderName = sender.DisplayName,
            senderAvatarUrl = sender.AvatarUrl,
            giftId = gift.Id, giftName = gift.Name, gift.ImageUrl, gift.AnimationUrl,
            gift.EffectType, gift.EffectTier, gift.EffectDurationMs,
            giftTransaction.Quantity, giftTransaction.TotalCoin, giftTransaction.CreatedAt
        }, cancellationToken);
        return Ok(Map(giftTransaction));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> History(Guid liveId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return Unauthorized();
        if (!await db.Lives.AnyAsync(x => x.Id == liveId && (x.HostUserId == userId || x.Status == LiveStatus.Live && x.Privacy == LivePrivacy.Public), cancellationToken)) return Forbid();
        return Ok(await db.LiveGiftTransactions.AsNoTracking().Where(x => x.LiveId == liveId)
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, x.SenderUserId, senderName = db.Users.Where(u => u.Id == x.SenderUserId).Select(u => u.DisplayName).FirstOrDefault(), x.GiftId,
                giftName = db.LiveGifts.Where(g => g.Id == x.GiftId).Select(g => g.Name).FirstOrDefault(), x.Quantity, x.TotalCoin, x.CreatedAt })
            .ToListAsync(cancellationToken));
    }

    private static object Map(LiveGiftTransaction value) => new { value.Id, value.RequestId, value.LiveId, value.GiftId, value.Quantity,
        value.UnitPriceCoin, value.TotalCoin, value.FeePercent, value.PlatformFee, value.HostEarning, value.CreatedAt };
}

public sealed record SendLiveGiftBody(Guid GiftId, int Quantity, Guid RequestId);
