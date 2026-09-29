using Viora.Infrastructure.LiveStreaming;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class InMemoryLiveReactionBufferTests
{
    [Fact]
    public void AggregatesReactionsPerLiveWithoutWritingEachTap()
    {
        var buffer = new InMemoryLiveReactionBuffer();
        var liveA = Guid.NewGuid();
        var liveB = Guid.NewGuid();

        Parallel.For(0, 1000, _ => buffer.Add(liveA, 1));
        buffer.Add(liveB, 7);

        Assert.Equal(1000, buffer.GetPendingCount(liveA));
        Assert.Equal(7, buffer.GetPendingCount(liveB));
        Assert.Equal(1000, buffer.TakePendingCount(liveA));
        Assert.Equal(0, buffer.GetPendingCount(liveA));
    }

    [Fact]
    public void RestoresAFailedFlushWithoutLosingNewReactions()
    {
        var buffer = new InMemoryLiveReactionBuffer();
        var liveId = Guid.NewGuid();
        buffer.Add(liveId, 12);

        var flushing = buffer.TakePendingCount(liveId);
        buffer.Add(liveId, 3);
        buffer.RestorePendingCount(liveId, flushing);

        Assert.Equal(15, buffer.GetPendingCount(liveId));
        Assert.Contains(liveId, buffer.PendingLiveIds());
    }
}
