using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Application.Wallets;
using Viora.Infrastructure.LiveStreaming;
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
        .Select(x => new LiveGiftDto(x.Id, x.Name, x.ImageUrl, x.AnimationUrl, x.Price, x.AnimationType, x.SortOrder, x.IsActive, db.LiveGiftTransactions.Count(g => g.GiftId == x.Id), x.EffectType, x.EffectTier, x.EffectDurationMs))
        .ToListAsync(cancellationToken));

    [Authorize(Roles = "2")]
    [HttpGet("admin")]
    public async Task<IActionResult> All(CancellationToken cancellationToken) => Ok(await db.LiveGifts.AsNoTracking()
        .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
        .Select(x => new LiveGiftDto(x.Id, x.Name, x.ImageUrl, x.AnimationUrl, x.Price, x.AnimationType, x.SortOrder, x.IsActive, db.LiveGiftTransactions.Count(g => g.GiftId == x.Id), x.EffectType, x.EffectTier, x.EffectDurationMs))
        .ToListAsync(cancellationToken));

    [Authorize(Roles = "2")]
    [HttpPost]
    public async Task<IActionResult> Create(LiveGiftBody body, CancellationToken cancellationToken)
    {
        var effectType = body.EffectType ?? LiveGiftEffectType.None;
        var effectTier = body.EffectTier ?? 0;
        var effectDurationMs = body.EffectDurationMs ?? 0;
        if (!Valid(body, effectType, effectTier, effectDurationMs)) return UnprocessableEntity(new { code = "LIVE_GIFT_INVALID" });
        var gift = new LiveGift { Name = body.Name.Trim(), ImageUrl = body.ImageUrl.Trim(), AnimationUrl = body.AnimationUrl?.Trim(), Price = body.Price, PriceCoin = 1, AnimationType = body.AnimationType, SortOrder = body.SortOrder, IsActive = body.IsActive, EffectType = effectType, EffectTier = effectTier, EffectDurationMs = effectDurationMs };
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
        gift.Price = body.Price; gift.AnimationType = body.AnimationType; gift.SortOrder = body.SortOrder; gift.IsActive = body.IsActive;
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

    private static bool Valid(LiveGiftBody body, LiveGiftEffectType effectType, short effectTier, int effectDurationMs) => !string.IsNullOrWhiteSpace(body.Name) && body.Name.Length <= 100 &&
        body.Price is > 0 and <= 9_007_199_254_740_991L && Enum.IsDefined(body.AnimationType) && Enum.IsDefined(effectType) &&
        (effectType == LiveGiftEffectType.None
            ? effectTier == 0 && effectDurationMs == 0
            : effectTier is >= 1 and <= 3 && effectDurationMs is >= 3000 and <= 7000) &&
        Uri.TryCreate(body.ImageUrl, UriKind.Absolute, out var image) && image.Scheme == Uri.UriSchemeHttps &&
        (body.AnimationUrl is null || Uri.TryCreate(body.AnimationUrl, UriKind.Absolute, out var animation) && animation.Scheme == Uri.UriSchemeHttps);
    private static LiveGiftDto Map(LiveGift gift, int useCount) => new(gift.Id, gift.Name, gift.ImageUrl, gift.AnimationUrl, gift.Price, gift.AnimationType, gift.SortOrder, gift.IsActive, useCount, gift.EffectType, gift.EffectTier, gift.EffectDurationMs);
}

public sealed record LiveGiftBody(string Name, string ImageUrl, string? AnimationUrl, long Price, LiveGiftAnimationType AnimationType, int SortOrder, bool IsActive, LiveGiftEffectType? EffectType = null, short? EffectTier = null, int? EffectDurationMs = null);
public sealed record LiveGiftDto(Guid Id, string Name, string ImageUrl, string? AnimationUrl, long Price, LiveGiftAnimationType AnimationType, int SortOrder, bool IsActive, int UseCount, LiveGiftEffectType EffectType, short EffectTier, int EffectDurationMs);

[ApiController]
[Route("api/lives/{liveId:guid}/gifts")]
public sealed class LiveGiftTransactionsController(AppDbContext db, IHubContext<RealtimeHub> realtime,
    LiveGiftWalletService payments, ILogger<LiveGiftTransactionsController> logger) : ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Send(Guid liveId, SendLiveGiftBody body, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return Unauthorized();
        if (Request.Headers["X-Live-Gift-Currency"] != "VND")
            return Conflict(new { code = "LIVE_GIFT_CLIENT_UPGRADE_REQUIRED", message = "Vui lòng cập nhật ứng dụng để gửi quà bằng ví VNĐ." });
        if (!long.TryParse(Request.Headers["X-Live-Gift-Expected-Price"], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var expectedPrice) || expectedPrice <= 0)
            return UnprocessableEntity(new { code = "LIVE_GIFT_INVALID", message = "Cần xác nhận giá quà VNĐ trước khi gửi." });
        LiveGiftPaymentResult result;
        try
        {
            result = await payments.SendAsync(userId, liveId, body.GiftId, body.Quantity, body.RequestId, cancellationToken, expectedPrice);
        }
        catch (InsufficientWalletBalanceException exception)
        {
            return Conflict(new { code = exception.Code, message = "Số dư của bạn không đủ để gửi món quà này.", balance = exception.Available });
        }
        catch (WalletValidationException exception)
        {
            return UnprocessableEntity(new { code = exception.Code, message = exception.Message });
        }
        catch (WalletConflictException exception)
        {
            return Conflict(new { code = exception.Code, message = exception.Message });
        }

        // SendAsync has committed both balances and both ledger entries before returning.
        if (!result.IsReplay)
        {
            var payment = result.Transaction;
            var gift = result.Gift;
            try
            {
                await realtime.Clients.Group($"live:{liveId:N}").SendAsync("LiveGift", new
                {
                    payment.Id, transactionId = payment.Id, liveId, senderUserId = userId,
                    receiverUserId = payment.HostUserId, senderName = result.SenderName, senderAvatarUrl = result.SenderAvatarUrl,
                    giftId = gift.Id, giftName = payment.GiftName, gift.ImageUrl, gift.AnimationUrl,
                    gift.EffectType, gift.EffectTier, gift.EffectDurationMs,
                    payment.Quantity, unitPrice = payment.UnitPrice, totalAmount = payment.GrossAmount, payment.CreatedAt
                }, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // A notification failure must not turn an already paid gift into an API payment failure.
                logger.LogWarning(exception, "Live gift notification failed for committed transaction {TransactionId}.", payment.Id);
            }
        }
        return Ok(Map(result));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> History(Guid liveId, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(liveId, cancellationToken)) return Forbid();
        return Ok(await db.LiveGiftTransactions.AsNoTracking().Where(x => x.LiveId == liveId)
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new
            {
                x.Id, transactionId = x.Id, x.SenderUserId, receiverUserId = x.HostUserId,
                senderName = db.Users.Where(u => u.Id == x.SenderUserId).Select(u => u.DisplayName).FirstOrDefault(),
                x.GiftId, giftName = x.GiftName ?? db.LiveGifts.Where(g => g.Id == x.GiftId).Select(g => g.Name).FirstOrDefault(),
                x.Quantity, x.Currency, totalAmount = x.GrossAmount, x.TotalCoin, x.CreatedAt
            }).ToListAsync(cancellationToken));
    }

    [Authorize]
    [HttpGet("top-gifters")]
    public async Task<IActionResult> TopGifters(Guid liveId, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(liveId, cancellationToken)) return Forbid();
        var totals = db.LiveGiftTransactions.AsNoTracking()
            .Where(x => x.LiveId == liveId && x.Currency == "VND" && x.Status == WalletTransactionStatus.Completed)
            .GroupBy(x => x.SenderUserId)
            .Select(group => new { senderUserId = group.Key, totalAmount = group.Sum(x => x.GrossAmount ?? 0), totalGiftCount = group.Sum(x => (long)x.Quantity) });
        return Ok(await totals.OrderByDescending(x => x.totalAmount).ThenBy(x => x.senderUserId).Take(100)
            .Join(db.Users.AsNoTracking(), total => total.senderUserId, user => user.Id,
                (total, user) => new { total.senderUserId, senderName = user.DisplayName, senderAvatarUrl = user.AvatarUrl, total.totalAmount, total.totalGiftCount })
            .OrderByDescending(x => x.totalAmount).ThenBy(x => x.senderUserId)
            .ToListAsync(cancellationToken));
    }

    [Authorize]
    [HttpGet("income")]
    public async Task<IActionResult> Income(Guid liveId, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(liveId, cancellationToken)) return Forbid();
        var gifts = db.LiveGiftTransactions.AsNoTracking().Where(x => x.LiveId == liveId && x.Currency == "VND" && x.Status == WalletTransactionStatus.Completed);
        var totals = await gifts.GroupBy(x => x.LiveId).Select(group => new
        {
            totalGiftCount = group.Sum(x => (long)x.Quantity),
            totalGiftValue = group.Sum(x => x.GrossAmount ?? 0),
            netAmount = group.Sum(x => x.NetAmount ?? 0),
            senderCount = group.Select(x => x.SenderUserId).Distinct().Count()
        }).SingleOrDefaultAsync(cancellationToken);
        return Ok(new { liveId, currency = "VND", totalGiftCount = totals?.totalGiftCount ?? 0,
            totalGiftValue = totals?.totalGiftValue ?? 0, netAmount = totals?.netAmount ?? 0, senderCount = totals?.senderCount ?? 0 });
    }

    private async Task<bool> CanReadAsync(Guid liveId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return false;
        var live = await db.Lives.AsNoTracking().SingleOrDefaultAsync(x => x.Id == liveId, cancellationToken);
        if (live is null) return false;
        if (live.HostUserId == userId) return true;
        if (live.Status != LiveStatus.Live || await db.LiveUserRestrictions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId && x.IsBlocked, cancellationToken)) return false;
        return live.Privacy switch
        {
            LivePrivacy.Public => true,
            LivePrivacy.Followers => await db.Follows.AnyAsync(x => x.FollowerId == userId && x.FollowingId == live.HostUserId, cancellationToken),
            LivePrivacy.Friends => await db.Friendships.AnyAsync(x => x.Status == FriendshipStatus.Accepted &&
                ((x.RequesterUserId == userId && x.AddresseeUserId == live.HostUserId) ||
                 (x.AddresseeUserId == userId && x.RequesterUserId == live.HostUserId)), cancellationToken),
            _ => false
        };
    }

    private static object Map(LiveGiftPaymentResult result)
    {
        var value = result.Transaction;
        return new
        {
            value.Id, transactionId = value.Id, value.RequestId, value.LiveId, value.GiftId, value.Quantity,
            value.UnitPrice, totalAmount = value.GrossAmount, value.GrossAmount, platformFee = value.FeeAmount,
            value.NetAmount, hostEarning = value.NetAmount, value.Currency, value.Status,
            senderBalance = result.SenderBalance, value.CreatedAt
        };
    }
}

public sealed record SendLiveGiftBody(Guid GiftId, int Quantity, Guid RequestId);
