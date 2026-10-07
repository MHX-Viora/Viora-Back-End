namespace Viora.Application.Live;

public sealed record LiveCommentEvent(
    Guid Id, Guid LiveId, Guid UserId, string Name, string? AvatarUrl,
    string Text, DateTimeOffset CreatedAt);

public enum LiveCommentAddResult { Added, Duplicate, RateLimited, Closed }

public sealed class LiveChatOptions
{
    public int RecentBufferSize { get; set; } = 200;
    public int ClientDisplayLimit { get; set; } = 100;
    public int MaxCommentLength { get; set; } = 500;
    public int RateLimitCount { get; set; } = 5;
    public int RateLimitWindowSeconds { get; set; } = 5;
    public int StatsFlushSeconds { get; set; } = 5;
}

public interface ILiveCommentBuffer
{
    LiveCommentAddResult TryAdd(LiveCommentEvent comment);
    IReadOnlyList<LiveCommentEvent> GetRecent(Guid liveId, int limit);
    LiveCommentEvent? Find(Guid liveId, Guid commentId);
    LiveCommentEvent? GetPinned(Guid liveId);
    LiveCommentEvent? SetPinned(Guid liveId, Guid? commentId);
    bool Remove(Guid liveId, Guid commentId);
    void Close(Guid liveId);
    IReadOnlyList<Guid> PendingLiveIds();
    long TakePendingCount(Guid liveId);
    void RestorePendingCount(Guid liveId, long count);
    void PruneClosed(DateTimeOffset now);
}

public interface ILiveReactionBuffer
{
    void Add(Guid liveId, int count);
    long GetPendingCount(Guid liveId);
    IReadOnlyList<Guid> PendingLiveIds();
    long TakePendingCount(Guid liveId);
    void RestorePendingCount(Guid liveId, long count);
}
