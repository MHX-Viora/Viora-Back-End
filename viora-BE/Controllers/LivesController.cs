using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Application.Live;
using Viora.Application.Posts;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Realtime;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/lives")]
public sealed class LivesController(AppDbContext db, IHubContext<RealtimeHub> realtime, IAgoraTokenService agoraTokens, IMediaStorage mediaStorage) : ControllerBase
{
    [Authorize]
    [HttpGet("config")]
    public IActionResult Config() => agoraTokens.IsConfigured
        ? Ok(new { appId = agoraTokens.AppId })
        : StatusCode(503, new { code = "AGORA_UNAVAILABLE", message = "Agora chưa được cấu hình." });

    [Authorize]
    [HttpPost("cover")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadCover(IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        if (file.Length is <= 0 or > 10_000_000 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { code = "LIVE_COVER_INVALID", message = "Ảnh bìa phải là ảnh dưới 10 MB." });
        await using var stream = file.OpenReadStream();
        var result = await mediaStorage.UploadPostImageAsync(userId, new CreatePostFile(stream, file.FileName, file.ContentType, file.Length), cancellationToken);
        return Ok(new { url = result.MediaUrl });
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await db.Lives
        .AsNoTracking().Where(x => x.Status == LiveStatus.Live && x.Privacy == LivePrivacy.Public)
        .OrderByDescending(x => x.StartedAt).Take(100)
        .Select(x => new LiveDto(x.Id, x.HostUserId, x.Host.DisplayName, x.Host.AvatarUrl,
            x.CategoryId, x.Category.Name, x.Title, x.CoverUrl, x.Privacy,
            x.AllowComments, x.AllowGifts, x.Status, x.StartedAt, x.EndedAt, x.CurrentViewerCount,
            x.PeakViewerCount, x.TotalViews, x.UniqueViewers))
        .ToListAsync(cancellationToken));

    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        return Ok(await db.Lives.AsNoTracking().Where(x => x.HostUserId == userId)
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new LiveDto(x.Id, x.HostUserId, x.Host.DisplayName, x.Host.AvatarUrl,
                x.CategoryId, x.Category.Name, x.Title, x.CoverUrl, x.Privacy,
                x.AllowComments, x.AllowGifts, x.Status, x.StartedAt, x.EndedAt, x.CurrentViewerCount,
                x.PeakViewerCount, x.TotalViews, x.UniqueViewers))
            .ToListAsync(cancellationToken));
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var live = await db.Lives.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new LiveDto(x.Id, x.HostUserId, x.Host.DisplayName, x.Host.AvatarUrl,
                x.CategoryId, x.Category.Name, x.Title, x.CoverUrl, x.Privacy,
                x.AllowComments, x.AllowGifts, x.Status, x.StartedAt, x.EndedAt, x.CurrentViewerCount,
                x.PeakViewerCount, x.TotalViews, x.UniqueViewers))
            .SingleOrDefaultAsync(cancellationToken);
        if (live is null) return NotFound();
        if (!await CanSeeAsync(live, userId, cancellationToken)) return Forbid();
        return Ok(live);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateLiveBody body, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var title = body.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100 || body.Description?.Length > 1000 ||
            (short)body.Privacy is < 0 or > 2 ||
            (body.CoverUrl is not null && (!Uri.TryCreate(body.CoverUrl, UriKind.Absolute, out var cover) || cover.Scheme != Uri.UriSchemeHttps)))
            return UnprocessableEntity(new { code = "LIVE_INVALID_SETUP", message = "Thông tin buổi Live không hợp lệ." });
        if (!await db.LiveCategories.AnyAsync(x => x.Id == body.CategoryId && x.IsActive, cancellationToken))
            return UnprocessableEntity(new { code = "LIVE_CATEGORY_INACTIVE", message = "Danh mục Live không còn hoạt động." });
        if (await db.Lives.AnyAsync(x => x.HostUserId == userId &&
            (x.Status == LiveStatus.Preparing || x.Status == LiveStatus.Live || x.Status == LiveStatus.Reconnecting), cancellationToken))
            return Conflict(new { code = "LIVE_ALREADY_ACTIVE", message = "Bạn đã có một buổi Live đang diễn ra." });

        var id = Guid.NewGuid();
        var live = new Live
        {
            Id = id, HostUserId = userId, CategoryId = body.CategoryId, Title = title,
            CoverUrl = body.CoverUrl, Description = body.Description?.Trim(), Privacy = body.Privacy,
            AllowComments = body.AllowComments, AllowGifts = body.AllowGifts,
            AgoraChannelName = $"live_{id:N}_{Guid.NewGuid():N}"[..46], Status = LiveStatus.Preparing
        };
        db.Lives.Add(live);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/lives/{id}", new { live.Id, live.Status });
    }

    [Authorize]
    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var live = await db.Lives.FindAsync([id], cancellationToken);
        if (live is null) return NotFound();
        if (live.HostUserId != userId) return Forbid();
        if (live.Status == LiveStatus.Live) return Ok(new { live.Id, live.Status, live.StartedAt });
        if (live.Status != LiveStatus.Preparing) return Conflict(new { code = "LIVE_INVALID_STATE" });
        live.Status = LiveStatus.Live;
        live.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (live.Privacy == LivePrivacy.Public)
            await realtime.Clients.All.SendAsync("LiveStarted", new { live.Id, live.HostUserId, live.CategoryId, live.Title, live.StartedAt }, cancellationToken);
        return Ok(new { live.Id, live.Status, live.StartedAt });
    }

    [Authorize]
    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var live = await db.Lives.FindAsync([id], cancellationToken);
        if (live is null) return NotFound();
        if (live.HostUserId != userId) return Forbid();
        if (live.Status is LiveStatus.Ended or LiveStatus.Cancelled) return Ok(new { live.Id, live.Status, live.EndedAt });
        if (live.Status is not (LiveStatus.Preparing or LiveStatus.Live or LiveStatus.Reconnecting))
            return Conflict(new { code = "LIVE_INVALID_STATE" });
        live.Status = live.Status == LiveStatus.Preparing ? LiveStatus.Cancelled : LiveStatus.Ended;
        live.EndedAt = DateTime.UtcNow;
        live.CurrentViewerCount = 0;
        var openSessions = await db.LiveViewerSessions.Where(x => x.LiveId == id && x.LeftAt == null).ToListAsync(cancellationToken);
        foreach (var session in openSessions)
        {
            session.LeftAt = live.EndedAt;
            session.DurationSeconds = Math.Max(0, (int)(live.EndedAt.Value - session.JoinedAt).TotalSeconds);
        }
        await db.SaveChangesAsync(cancellationToken);
        await realtime.Clients.Group($"live:{id:N}").SendAsync("LiveEnded", new { live.Id, live.Status, live.EndedAt }, cancellationToken);
        if (live.Privacy == LivePrivacy.Public)
            await realtime.Clients.All.SendAsync("LiveEnded", new { live.Id, live.Status, live.EndedAt }, cancellationToken);
        return Ok(new { live.Id, live.Status, live.EndedAt });
    }

    [Authorize]
    [HttpPost("{id:guid}/token")]
    [HttpPost("{id:guid}/token/renew")]
    public async Task<IActionResult> Token(Guid id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        if (!agoraTokens.IsConfigured) return StatusCode(503, new { code = "AGORA_UNAVAILABLE", message = "Agora chưa được cấu hình." });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize participant UID allocation across all API instances for this Live.
        var live = (await db.Lives.FromSqlInterpolated($"SELECT * FROM \"Lives\" WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (live is null) return NotFound();
        var host = live.HostUserId == userId;
        if (host ? live.Status is not (LiveStatus.Preparing or LiveStatus.Live or LiveStatus.Reconnecting)
                 : !await CanJoinAsync(live, userId, cancellationToken))
            return Forbid();
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.Account.Status == AccountStatus.Active, cancellationToken))
            return Forbid();

        var participant = await db.LiveAgoraParticipants.SingleOrDefaultAsync(x => x.LiveId == id && x.UserId == userId, cancellationToken);
        if (participant is null)
        {
            if (live.NextAgoraUid > uint.MaxValue) return StatusCode(503, new { code = "AGORA_UID_EXHAUSTED" });
            participant = new LiveAgoraParticipant { LiveId = id, UserId = userId, AgoraUid = live.NextAgoraUid++ };
            db.LiveAgoraParticipants.Add(participant);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Ok(agoraTokens.Issue(live.AgoraChannelName, participant.AgoraUid, host));
    }

    private bool TryUserId(out Guid id) => Guid.TryParse(User.FindFirstValue("user_id"), out id);

    private async Task<bool> CanSeeAsync(LiveDto live, Guid userId, CancellationToken cancellationToken)
    {
        if (live.HostUserId == userId) return true;
        if (live.Status != LiveStatus.Live ||
            await db.LiveUserRestrictions.AnyAsync(x => x.LiveId == live.Id && x.UserId == userId && x.IsBlocked, cancellationToken)) return false;
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

    private async Task<bool> CanJoinAsync(Live live, Guid userId, CancellationToken cancellationToken)
    {
        if (live.Status != LiveStatus.Live ||
            await db.LiveUserRestrictions.AnyAsync(x => x.LiveId == live.Id && x.UserId == userId && x.IsBlocked, cancellationToken)) return false;
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
}

public sealed record CreateLiveBody(string Title, Guid CategoryId, string? CoverUrl,
    string? Description = null, LivePrivacy Privacy = LivePrivacy.Public,
    bool AllowComments = true, bool AllowGifts = true);

public sealed record LiveDto(Guid Id, Guid HostUserId, string HostName, string? HostAvatarUrl,
    Guid CategoryId, string CategoryName, string Title, string? CoverUrl,
    LivePrivacy Privacy, bool AllowComments, bool AllowGifts, LiveStatus Status,
    DateTime? StartedAt, DateTime? EndedAt, int CurrentViewerCount, int PeakViewerCount,
    long TotalViews, int UniqueViewers);
