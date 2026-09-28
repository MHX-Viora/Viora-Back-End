using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Viora.Application.Live;
using Viora.Domain.Entities;
using Viora.Infrastructure.LiveStreaming;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Realtime;

[Authorize]
public sealed class RealtimeHub(
    IConnectionRegistry connections,
    AppDbContext dbContext,
    ILiveCommentBuffer commentBuffer,
    IOptions<LiveChatOptions> chatOptions,
    ILogger<RealtimeHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId))
        {
            connections.Add(userId, Context.ConnectionId);
            logger.LogInformation(
                "Realtime connected. UserId: {UserId}, ConnectionId: {ConnectionId}.",
                userId,
                Context.ConnectionId);
        }
        else
        {
            logger.LogWarning(
                "Realtime connected without valid user_id claim. ConnectionId: {ConnectionId}.",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveAllLives();
        if (TryGetUserId(out var userId))
        {
            connections.Remove(userId, Context.ConnectionId);
            logger.LogInformation(
                "Realtime disconnected. UserId: {UserId}, ConnectionId: {ConnectionId}, Error: {Error}.",
                userId,
                Context.ConnectionId,
                exception?.Message);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGroup(string groupName)
    {
        if (!TryGetUserId(out var userId) || !TryParseConversationGroupName(groupName, out var conversationId))
        {
            logger.LogWarning(
                "Realtime join group rejected because group name is invalid. GroupName: {GroupName}, ConnectionId: {ConnectionId}.",
                groupName,
                Context.ConnectionId);
            throw new HubException("Conversation group is invalid.");
        }

        var canJoin = await dbContext.ConversationMembers.AsNoTracking().AnyAsync(member =>
            member.ConversationId == conversationId &&
            member.UserId == userId &&
            member.Status == ConversationMemberStatus.Active &&
            member.Conversation.DeletedAt == null);
        if (!canJoin)
        {
            logger.LogWarning(
                "Realtime join group rejected because user is not an active member. UserId: {UserId}, ConversationId: {ConversationId}, ConnectionId: {ConnectionId}.",
                userId,
                conversationId,
                Context.ConnectionId);
            throw new HubException("You are not an active member of this conversation.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        logger.LogInformation(
            "Realtime joined group. UserId: {UserId}, ConversationId: {ConversationId}, GroupName: {GroupName}, ConnectionId: {ConnectionId}.",
            userId,
            conversationId,
            groupName,
            Context.ConnectionId);
    }

    public Task LeaveGroup(string groupName) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    public async Task<object> JoinLive(Guid liveId)
    {
        if (!TryGetUserId(out var userId)) throw new HubException("Authentication required.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var live = (await dbContext.Lives.FromSqlInterpolated($"SELECT * FROM \"Lives\" WHERE \"Id\" = {liveId} FOR UPDATE").ToListAsync()).SingleOrDefault();
        if (live is null || !await CanJoinLive(live, userId)) throw new HubException("Live unavailable.");
        if (userId != live.HostUserId)
        {
            var existing = await dbContext.LiveViewerSessions.AnyAsync(x => x.LiveId == liveId && x.ConnectionId == Context.ConnectionId && x.LeftAt == null);
            if (!existing)
            {
                var seen = await dbContext.LiveViewerSessions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId);
                dbContext.LiveViewerSessions.Add(new LiveViewerSession { LiveId = liveId, UserId = userId, ConnectionId = Context.ConnectionId, JoinedAt = DateTime.UtcNow });
                live.TotalViews++;
                if (!seen) live.UniqueViewers++;
                await dbContext.SaveChangesAsync();
                live.CurrentViewerCount = await dbContext.LiveViewerSessions.Where(x => x.LiveId == liveId && x.LeftAt == null).Select(x => x.UserId).Distinct().CountAsync();
                live.PeakViewerCount = Math.Max(live.PeakViewerCount, live.CurrentViewerCount);
                await dbContext.SaveChangesAsync();
            }
        }
        await transaction.CommitAsync();
        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroup(liveId));
        await Clients.Group(LiveGroup(liveId)).SendAsync("LiveViewerCount", new { liveId, count = live.CurrentViewerCount });
        var comments = commentBuffer.GetRecent(liveId, chatOptions.Value.ClientDisplayLimit);
        return new { viewerCount = live.CurrentViewerCount, comments };
    }

    public async Task LeaveLive(Guid liveId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroup(liveId));
        await CloseLiveSession(liveId);
    }

    public async Task<object> SendLiveComment(Guid liveId, string text)
    {
        if (!TryGetUserId(out var userId)) throw new HubException("Authentication required.");
        text = text?.Trim() ?? "";
        if (text.Length < 1 || text.Length > chatOptions.Value.MaxCommentLength) throw new HubException("Comment must be 1 to 500 characters.");
        var live = await dbContext.Lives.AsNoTracking().SingleOrDefaultAsync(x => x.Id == liveId);
        if (live is null || !live.AllowComments || !await CanJoinLive(live, userId)) throw new HubException("Comments unavailable.");
        if (userId != live.HostUserId && !await dbContext.LiveViewerSessions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId && x.ConnectionId == Context.ConnectionId && x.LeftAt == null))
            throw new HubException("Join Live before commenting.");
        if (await dbContext.LiveUserRestrictions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId && x.IsMuted && (x.MutedUntil == null || x.MutedUntil > DateTime.UtcNow)))
            throw new HubException("Commenting is muted.");
        var user = await dbContext.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.DisplayName, x.AvatarUrl }).SingleAsync();
        var payload = new LiveCommentEvent(Guid.NewGuid(), liveId, userId, user.DisplayName, user.AvatarUrl, text, DateTimeOffset.UtcNow);
        var result = commentBuffer.TryAdd(payload);
        if (result == LiveCommentAddResult.RateLimited) throw new HubException("Bạn gửi bình luận quá nhanh. Vui lòng thử lại sau.");
        if (result != LiveCommentAddResult.Added) throw new HubException("Comments unavailable.");
        await Clients.Group(LiveGroup(liveId)).SendAsync("LiveComment", payload);
        return payload;
    }

    public async Task DeleteLiveComment(Guid liveId, Guid commentId)
    {
        if (!TryGetUserId(out var userId) || !await CanModerateLive(liveId, userId)) throw new HubException("Moderator access required.");
        if (!commentBuffer.Remove(liveId, commentId)) throw new HubException("Comment no longer available.");
        await Clients.Group(LiveGroup(liveId)).SendAsync("LiveCommentDeleted", new { liveId, commentId });
    }

    public async Task MuteLiveUser(Guid liveId, Guid targetUserId)
    {
        if (!TryGetUserId(out var userId) || !await CanModerateLive(liveId, userId)) throw new HubException("Moderator access required.");
        var live = await dbContext.Lives.AsNoTracking().SingleAsync(x => x.Id == liveId);
        if (targetUserId == live.HostUserId || !await dbContext.Users.AnyAsync(x => x.Id == targetUserId)) throw new HubException("Invalid user.");
        var restriction = await dbContext.LiveUserRestrictions.SingleOrDefaultAsync(x => x.LiveId == liveId && x.UserId == targetUserId);
        if (restriction is null)
        {
            restriction = new LiveUserRestriction { LiveId = liveId, UserId = targetUserId, AppliedByUserId = userId };
            dbContext.LiveUserRestrictions.Add(restriction);
        }
        restriction.IsMuted = true;
        restriction.MutedUntil = null;
        restriction.AppliedByUserId = userId;
        await dbContext.SaveChangesAsync();
        await Clients.Group(LiveGroup(liveId)).SendAsync("LiveUserMuted", new { liveId, userId = targetUserId });
    }

    public async Task ReportLiveComment(Guid liveId, Guid commentId, ReportReason reason)
    {
        if (!TryGetUserId(out var userId) || !Enum.IsDefined(reason)) throw new HubException("Invalid report.");
        var live = await dbContext.Lives.AsNoTracking().SingleOrDefaultAsync(x => x.Id == liveId);
        if (live is null || !await CanJoinLive(live, userId)) throw new HubException("Live unavailable.");
        var comment = commentBuffer.Find(liveId, commentId);
        if (comment is null || comment.UserId == userId) throw new HubException("Comment no longer available.");
        if (await dbContext.Reports.AnyAsync(x => x.ReporterUserId == userId && x.TargetType == ReportTargetType.LiveComment && x.TargetId == commentId)) return;
        dbContext.Reports.Add(new Report { ReporterUserId = userId, TargetId = commentId, TargetType = ReportTargetType.LiveComment,
            Reason = reason, Description = JsonSerializer.Serialize(comment) });
        await dbContext.SaveChangesAsync();
    }

    private async Task<bool> CanModerateLive(Guid liveId, Guid userId)
    {
        var live = await dbContext.Lives.AsNoTracking().Where(x => x.Id == liveId)
            .Select(x => new { x.Status, x.HostUserId }).SingleOrDefaultAsync();
        if (live?.Status != LiveStatus.Live ||
            !await dbContext.Users.AnyAsync(x => x.Id == userId && x.Account.Status == AccountStatus.Active)) return false;
        return live.HostUserId == userId ||
            await dbContext.LiveModerators.AnyAsync(x => x.LiveId == liveId && x.UserId == userId);
    }

    public async Task ReactToLive(Guid liveId, int count)
    {
        if (!TryGetUserId(out var userId) || count is < 1 or > 20) throw new HubException("Invalid reaction.");
        var live = await dbContext.Lives.AsNoTracking().SingleOrDefaultAsync(x => x.Id == liveId);
        if (live is null || !await CanJoinLive(live, userId)) throw new HubException("Live unavailable.");
        if (userId != live.HostUserId && !await dbContext.LiveViewerSessions.AnyAsync(x => x.LiveId == liveId && x.UserId == userId && x.ConnectionId == Context.ConnectionId && x.LeftAt == null))
            throw new HubException("Join Live before reacting.");
        await dbContext.Lives.Where(x => x.Id == liveId).ExecuteUpdateAsync(x => x.SetProperty(l => l.TotalReactions, l => l.TotalReactions + count));
        await Clients.Group(LiveGroup(liveId)).SendAsync("LiveReaction", new { liveId, count });
    }

    private async Task LeaveAllLives()
    {
        var ids = await dbContext.LiveViewerSessions.Where(x => x.ConnectionId == Context.ConnectionId && x.LeftAt == null).Select(x => x.LiveId).Distinct().ToListAsync();
        foreach (var id in ids) await CloseLiveSession(id);
    }

    private async Task CloseLiveSession(Guid liveId)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var live = (await dbContext.Lives.FromSqlInterpolated($"SELECT * FROM \"Lives\" WHERE \"Id\" = {liveId} FOR UPDATE").ToListAsync()).SingleOrDefault();
        if (live is null) return;
        var sessions = await dbContext.LiveViewerSessions.Where(x => x.LiveId == liveId && x.ConnectionId == Context.ConnectionId && x.LeftAt == null).ToListAsync();
        if (sessions.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var session in sessions) { session.LeftAt = now; session.DurationSeconds = Math.Max(0, (int)(now - session.JoinedAt).TotalSeconds); }
        await dbContext.SaveChangesAsync();
        live.CurrentViewerCount = await dbContext.LiveViewerSessions.Where(x => x.LiveId == liveId && x.LeftAt == null).Select(x => x.UserId).Distinct().CountAsync();
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await Clients.Group(LiveGroup(liveId)).SendAsync("LiveViewerCount", new { liveId, count = live.CurrentViewerCount });
    }

    private async Task<bool> CanJoinLive(Live live, Guid userId)
    {
        if (live.Status != LiveStatus.Live ||
            !await dbContext.Users.AnyAsync(x => x.Id == userId && x.Account.Status == AccountStatus.Active) ||
            await dbContext.LiveUserRestrictions.AnyAsync(x => x.LiveId == live.Id && x.UserId == userId && x.IsBlocked)) return false;
        if (live.HostUserId == userId || live.Privacy == LivePrivacy.Public) return true;
        if (live.Privacy == LivePrivacy.Followers)
            return await dbContext.Follows.AnyAsync(x => x.FollowerId == userId && x.FollowingId == live.HostUserId);
        return live.Privacy == LivePrivacy.Friends && await dbContext.Friendships.AnyAsync(x => x.Status == FriendshipStatus.Accepted &&
            ((x.RequesterUserId == userId && x.AddresseeUserId == live.HostUserId) || (x.AddresseeUserId == userId && x.RequesterUserId == live.HostUserId)));
    }

    private static string LiveGroup(Guid id) => $"live:{id:N}";

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out userId);
    }

    private static bool TryParseConversationGroupName(string groupName, out Guid conversationId)
    {
        const string Prefix = "conversation:";

        if (groupName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            groupName = groupName[Prefix.Length..];
        }

        return Guid.TryParse(groupName, out conversationId);
    }
}
