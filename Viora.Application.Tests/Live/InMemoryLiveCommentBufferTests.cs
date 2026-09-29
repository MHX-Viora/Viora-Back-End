using Microsoft.Extensions.Options;
using Viora.Application.Live;
using Viora.Infrastructure.LiveStreaming;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class InMemoryLiveCommentBufferTests
{
    private static InMemoryLiveCommentBuffer CreateBuffer() => new(Options.Create(new LiveChatOptions()));

    private static LiveCommentEvent Comment(Guid liveId, Guid userId, int number, DateTimeOffset time) =>
        new(Guid.NewGuid(), liveId, userId, $"User {number}", null, $"Comment {number}", time);

    [Fact]
    public void KeepsTheLatestCommentsAndIsolatesLives()
    {
        var buffer = CreateBuffer();
        var liveA = Guid.NewGuid();
        var liveB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 250; index++)
            Assert.Equal(LiveCommentAddResult.Added, buffer.TryAdd(Comment(liveA, Guid.NewGuid(), index, now)));
        buffer.TryAdd(Comment(liveB, Guid.NewGuid(), 1, now));

        Assert.Equal(200, buffer.GetRecent(liveA, 200).Count);
        Assert.Equal("Comment 50", buffer.GetRecent(liveA, 200)[0].Text);
        Assert.Equal("Comment 150", buffer.GetRecent(liveA, 100)[0].Text);
        Assert.Single(buffer.GetRecent(liveB, 100));
        Assert.Equal(250, buffer.TakePendingCount(liveA));
    }

    [Fact]
    public void ConcurrentAddsStayBoundedAndCountEveryAcceptedEvent()
    {
        var buffer = CreateBuffer();
        var liveId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Parallel.For(0, 1000, index => buffer.TryAdd(Comment(liveId, Guid.NewGuid(), index, now)));

        Assert.Equal(200, buffer.GetRecent(liveId, 200).Count);
        Assert.Equal(1000, buffer.TakePendingCount(liveId));
    }

    [Fact]
    public void RateLimitsPerUserAndLiveWithoutClosingTheRoom()
    {
        var buffer = CreateBuffer();
        var liveId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 5; index++)
            Assert.Equal(LiveCommentAddResult.Added, buffer.TryAdd(Comment(liveId, userId, index, now)));

        Assert.Equal(LiveCommentAddResult.RateLimited, buffer.TryAdd(Comment(liveId, userId, 6, now)));
        Assert.Equal(LiveCommentAddResult.Added, buffer.TryAdd(Comment(Guid.NewGuid(), userId, 7, now)));
        Assert.Equal(LiveCommentAddResult.Added, buffer.TryAdd(Comment(liveId, userId, 8, now.AddSeconds(6))));
    }

    [Fact]
    public void DuplicateEventDoesNotConsumeRateLimitOrCountTwice()
    {
        var buffer = CreateBuffer();
        var liveId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = Comment(liveId, userId, 0, now);
        Assert.Equal(LiveCommentAddResult.Added, buffer.TryAdd(first));
        Assert.Equal(LiveCommentAddResult.Duplicate, buffer.TryAdd(first));
        for (var index = 1; index < 5; index++)
            Assert.Equal(LiveCommentAddResult.Added, buffer.TryAdd(Comment(liveId, userId, index, now)));
        Assert.Equal(LiveCommentAddResult.RateLimited, buffer.TryAdd(Comment(liveId, userId, 6, now)));
        Assert.Equal(5, buffer.TakePendingCount(liveId));
    }

    [Fact]
    public void DeleteAndCloseRemoveTransientHistory()
    {
        var buffer = CreateBuffer();
        var liveId = Guid.NewGuid();
        var comment = Comment(liveId, Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        buffer.TryAdd(comment);

        Assert.True(buffer.Remove(liveId, comment.Id));
        Assert.Empty(buffer.GetRecent(liveId, 100));
        buffer.Close(liveId);
        Assert.Equal(LiveCommentAddResult.Closed, buffer.TryAdd(Comment(liveId, Guid.NewGuid(), 2, DateTimeOffset.UtcNow)));
        Assert.Equal(1, buffer.TakePendingCount(liveId));
    }

    [Fact]
    public void PinSurvivesRecentCommentEvictionAndCanBeReplacedOrCleared()
    {
        var buffer = CreateBuffer();
        var liveId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = Comment(liveId, Guid.NewGuid(), 1, now);
        var second = Comment(liveId, Guid.NewGuid(), 2, now);
        buffer.TryAdd(first);
        buffer.TryAdd(second);

        Assert.Equal(first, buffer.SetPinned(liveId, first.Id));
        Assert.Null(buffer.GetPinned(Guid.NewGuid()));
        for (var index = 0; index < 210; index++)
            buffer.TryAdd(Comment(liveId, Guid.NewGuid(), index + 3, now));
        Assert.Equal(first, buffer.GetPinned(liveId));
        Assert.DoesNotContain(buffer.GetRecent(liveId, 200), item => item.Id == first.Id);
        Assert.Null(buffer.SetPinned(liveId, second.Id)); // second has left the bounded history
        Assert.Equal(first, buffer.GetPinned(liveId));
        var newest = buffer.GetRecent(liveId, 1)[0];
        Assert.Equal(newest, buffer.SetPinned(liveId, newest.Id));
        Assert.Null(buffer.SetPinned(liveId, null));
        Assert.Null(buffer.GetPinned(liveId));
    }

    [Fact]
    public void DeletingOrClosingAPinnedCommentClearsThePin()
    {
        var buffer = CreateBuffer();
        var liveId = Guid.NewGuid();
        var comment = Comment(liveId, Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        buffer.TryAdd(comment);
        buffer.SetPinned(liveId, comment.Id);
        Assert.True(buffer.Remove(liveId, comment.Id));
        Assert.Null(buffer.GetPinned(liveId));
        buffer.TryAdd(Comment(liveId, Guid.NewGuid(), 2, DateTimeOffset.UtcNow));
        var next = buffer.GetRecent(liveId, 1)[0];
        buffer.SetPinned(liveId, next.Id);
        buffer.Close(liveId);
        Assert.Null(buffer.GetPinned(liveId));
    }
}
