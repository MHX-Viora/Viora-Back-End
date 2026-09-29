using System.Collections.Concurrent;
using Viora.Application.Live;

namespace Viora.Infrastructure.LiveStreaming;

public sealed class InMemoryLiveReactionBuffer : ILiveReactionBuffer
{
    private sealed class Counter
    {
        public readonly object Gate = new();
        public long Pending;
    }

    private readonly ConcurrentDictionary<Guid, Counter> counters = new();

    public void Add(Guid liveId, int count)
    {
        if (count <= 0) return;
        var counter = counters.GetOrAdd(liveId, _ => new Counter());
        lock (counter.Gate) counter.Pending += count;
    }

    public long GetPendingCount(Guid liveId)
    {
        if (!counters.TryGetValue(liveId, out var counter)) return 0;
        lock (counter.Gate) return counter.Pending;
    }

    public IReadOnlyList<Guid> PendingLiveIds() => counters.Where(pair =>
    {
        lock (pair.Value.Gate) return pair.Value.Pending > 0;
    }).Select(pair => pair.Key).ToArray();

    public long TakePendingCount(Guid liveId)
    {
        if (!counters.TryGetValue(liveId, out var counter)) return 0;
        lock (counter.Gate)
        {
            var count = counter.Pending;
            counter.Pending = 0;
            return count;
        }
    }

    public void RestorePendingCount(Guid liveId, long count)
    {
        if (count <= 0) return;
        var counter = counters.GetOrAdd(liveId, _ => new Counter());
        lock (counter.Gate) counter.Pending += count;
    }
}
