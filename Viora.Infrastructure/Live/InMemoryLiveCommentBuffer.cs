using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Viora.Application.Live;

namespace Viora.Infrastructure.LiveStreaming;

public sealed class InMemoryLiveCommentBuffer(IOptions<LiveChatOptions> options) : ILiveCommentBuffer
{
    private sealed class Room
    {
        public readonly object Gate = new();
        public readonly List<LiveCommentEvent> Comments = [];
        public readonly Dictionary<Guid, Queue<DateTimeOffset>> SentAt = [];
        public LiveCommentEvent? PinnedComment;
        public long PendingCount;
        public int AddsSinceRateSweep;
        public DateTimeOffset? ClosedAt;
    }

    private readonly ConcurrentDictionary<Guid, Room> rooms = new();
    private readonly LiveChatOptions settings = options.Value;

    public LiveCommentAddResult TryAdd(LiveCommentEvent comment)
    {
        var room = rooms.GetOrAdd(comment.LiveId, _ => new Room());
        lock (room.Gate)
        {
            if (room.ClosedAt is not null) return LiveCommentAddResult.Closed;
            if (room.Comments.Any(item => item.Id == comment.Id)) return LiveCommentAddResult.Duplicate;
            if (!room.SentAt.TryGetValue(comment.UserId, out var recent))
                room.SentAt[comment.UserId] = recent = new Queue<DateTimeOffset>();
            var earliest = comment.CreatedAt.AddSeconds(-settings.RateLimitWindowSeconds);
            while (recent.Count > 0 && recent.Peek() <= earliest) recent.Dequeue();
            if (recent.Count >= settings.RateLimitCount) return LiveCommentAddResult.RateLimited;
            recent.Enqueue(comment.CreatedAt);
            if (++room.AddsSinceRateSweep >= 256)
            {
                room.AddsSinceRateSweep = 0;
                foreach (var pair in room.SentAt.ToArray())
                {
                    while (pair.Value.Count > 0 && pair.Value.Peek() <= earliest) pair.Value.Dequeue();
                    if (pair.Value.Count == 0) room.SentAt.Remove(pair.Key);
                }
            }
            room.Comments.Add(comment);
            if (room.Comments.Count > settings.RecentBufferSize) room.Comments.RemoveAt(0);
            room.PendingCount++;
            return LiveCommentAddResult.Added;
        }
    }

    public IReadOnlyList<LiveCommentEvent> GetRecent(Guid liveId, int limit)
    {
        if (!rooms.TryGetValue(liveId, out var room)) return [];
        lock (room.Gate)
            return room.Comments.TakeLast(Math.Clamp(limit, 0, settings.RecentBufferSize)).ToArray();
    }

    public LiveCommentEvent? Find(Guid liveId, Guid commentId)
    {
        if (!rooms.TryGetValue(liveId, out var room)) return null;
        lock (room.Gate) return room.Comments.Find(item => item.Id == commentId) ??
            (room.PinnedComment?.Id == commentId ? room.PinnedComment : null);
    }

    public LiveCommentEvent? GetPinned(Guid liveId)
    {
        if (!rooms.TryGetValue(liveId, out var room)) return null;
        lock (room.Gate) return room.PinnedComment;
    }

    public LiveCommentEvent? SetPinned(Guid liveId, Guid? commentId)
    {
        if (!rooms.TryGetValue(liveId, out var room)) return null;
        lock (room.Gate)
        {
            if (room.ClosedAt is not null) return null;
            if (commentId is null) return room.PinnedComment = null;
            var comment = room.Comments.Find(item => item.Id == commentId) ??
                (room.PinnedComment?.Id == commentId ? room.PinnedComment : null);
            if (comment is not null) room.PinnedComment = comment;
            return comment;
        }
    }

    public bool Remove(Guid liveId, Guid commentId)
    {
        if (!rooms.TryGetValue(liveId, out var room)) return false;
        lock (room.Gate)
        {
            var index = room.Comments.FindIndex(item => item.Id == commentId);
            if (index < 0 && room.PinnedComment?.Id != commentId) return false;
            if (index >= 0) room.Comments.RemoveAt(index);
            if (room.PinnedComment?.Id == commentId) room.PinnedComment = null;
            return true;
        }
    }

    public void Close(Guid liveId)
    {
        var room = rooms.GetOrAdd(liveId, _ => new Room());
        lock (room.Gate)
        {
            room.ClosedAt ??= DateTimeOffset.UtcNow;
            room.Comments.Clear();
            room.PinnedComment = null;
            room.SentAt.Clear();
        }
    }

    public IReadOnlyList<Guid> PendingLiveIds() => rooms.Where(pair =>
    {
        lock (pair.Value.Gate) return pair.Value.PendingCount > 0;
    }).Select(pair => pair.Key).ToArray();

    public long TakePendingCount(Guid liveId)
    {
        if (!rooms.TryGetValue(liveId, out var room)) return 0;
        lock (room.Gate)
        {
            var count = room.PendingCount;
            room.PendingCount = 0;
            return count;
        }
    }

    public void RestorePendingCount(Guid liveId, long count)
    {
        if (count <= 0) return;
        var room = rooms.GetOrAdd(liveId, _ => new Room());
        lock (room.Gate) room.PendingCount += count;
    }

    public void PruneClosed(DateTimeOffset now)
    {
        foreach (var pair in rooms)
        {
            lock (pair.Value.Gate)
            {
                if (pair.Value.ClosedAt < now.AddMinutes(-10) && pair.Value.PendingCount == 0)
                    rooms.TryRemove(new KeyValuePair<Guid, Room>(pair.Key, pair.Value));
            }
        }
    }
}
