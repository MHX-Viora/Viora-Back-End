using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Live;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Realtime;
using Viora.Infrastructure.LiveStreaming;

namespace Viora.Infrastructure.LiveStreaming;

public sealed record LiveEndResult(Guid Id, LiveStatus Status, DateTime? EndedAt, bool Changed);

public sealed class LiveSessionFinalizer(AppDbContext db, IHubContext<RealtimeHub> realtime,
    ILiveCommentBuffer commentBuffer, LiveCommentCountFlusher commentCounts,
    LiveReactionCountFlusher reactionCounts,
    IOptions<LiveLifecycleOptions> lifecycleOptions)
{
    public async Task<LiveEndResult?> EndAsync(Guid id, bool onlyIfStale, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var live = (await db.Lives.FromSqlInterpolated($"SELECT * FROM \"Lives\" WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (live is null) return null;

        if (live.Status is LiveStatus.Ended or LiveStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            commentBuffer.Close(id);
            await commentCounts.FlushAsync(id, cancellationToken);
            await reactionCounts.FlushAsync(id, cancellationToken);
            return new LiveEndResult(id, live.Status, live.EndedAt, false);
        }
        if (live.Status is not (LiveStatus.Preparing or LiveStatus.Live or LiveStatus.Reconnecting) ||
            (onlyIfStale && !LiveHostLease.IsStale(live.Status, live.HostLastSeenAt, live.StartedAt,
                live.CreatedAt, DateTime.UtcNow, lifecycleOptions.Value)))
            return new LiveEndResult(id, live.Status, live.EndedAt, false);

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
        await transaction.CommitAsync(cancellationToken);

        commentBuffer.Close(id);
        await commentCounts.FlushAsync(id, cancellationToken);
        await reactionCounts.FlushAsync(id, cancellationToken);
        await realtime.Clients.Group($"live:{id:N}").SendAsync("LiveEnded", new { live.Id, live.Status, live.EndedAt }, cancellationToken);
        if (live.Privacy == LivePrivacy.Public)
            await realtime.Clients.All.SendAsync("LiveEnded", new { live.Id, live.Status, live.EndedAt }, cancellationToken);
        return new LiveEndResult(id, live.Status, live.EndedAt, true);
    }
}
