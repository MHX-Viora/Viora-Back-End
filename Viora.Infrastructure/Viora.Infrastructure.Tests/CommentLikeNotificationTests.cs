using Viora.Application.Notifications;
using Viora.Application.Posts;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CommentLikeNotificationTests
{
    [Fact]
    public async Task Comment_like_notification_uses_post_id_as_reference_id()
    {
        var postId = Guid.NewGuid();
        var owner = new User { Id = Guid.NewGuid(), DisplayName = "Lan" };
        var liker = new User { Id = Guid.NewGuid(), DisplayName = "Nam" };
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            UserId = owner.Id,
            User = owner,
            Content = "comment",
            Status = CommentStatus.Published
        };
        var repository = new CommentLikeRepository(liker, comment);
        var handler = new ToggleCommentLikeHandler(
            repository,
            new ToggleCommentLikeValidator(),
            new CaptureNotificationService(),
            new NoOpRealtimeService());

        var result = await handler.Handle(new ToggleCommentLikeCommand(liker.Id, comment.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Notification);
        Assert.Equal(postId, repository.Notification!.ReferenceId);
        Assert.NotEqual(comment.Id, repository.Notification.ReferenceId);
        Assert.Equal(NotificationReferenceType.Comment, repository.Notification.ReferenceType);
    }

    private sealed class CaptureNotificationService : INotificationService
    {
        public Task<Notification> SendAsync(SendNotificationCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PublishAsync(Notification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoOpRealtimeService : IRealtimeService
    {
        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CommentLikeRepository(User liker, Comment comment) : IPostInteractionRepository
    {
        public Notification? Notification { get; private set; }

        public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(userId == liker.Id ? liker : null);

        public Task<Comment?> GetCommentForLikeAsync(Guid commentId, CancellationToken cancellationToken) =>
            Task.FromResult<Comment?>(commentId == comment.Id ? comment : null);

        public Task<CommentReaction?> GetCommentReactionAsync(Guid commentId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<CommentReaction?>(null);

        public Task AddCommentReactionAsync(CommentReaction reaction, CancellationToken cancellationToken) => Task.CompletedTask;
        public void RemoveCommentReaction(CommentReaction reaction) { }

        public Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken)
        {
            Notification = notification;
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<bool> CanViewPostAsync(Post post, Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Post?> GetPostForInteractionAsync(Guid postId, CancellationToken cancellationToken) => Task.FromResult<Post?>(null);
        public Task<Post?> GetPostWithOriginalAsync(Guid postId, CancellationToken cancellationToken) => Task.FromResult<Post?>(null);
        public Task<Comment?> GetCommentForReplyAsync(Guid commentId, CancellationToken cancellationToken) => Task.FromResult<Comment?>(null);
        public Task<Comment?> GetCommentForDeleteAsync(Guid commentId, CancellationToken cancellationToken) => Task.FromResult<Comment?>(null);
        public Task<PostReaction?> GetReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<PostReaction?>(null);
        public Task<SavedPost?> GetSavedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<SavedPost?>(null);
        public Task<bool> HasReportedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddReactionAsync(PostReaction reaction, CancellationToken cancellationToken) => Task.CompletedTask;
        public void RemoveReaction(PostReaction reaction) { }
        public Task AddCommentAsync(Comment comment, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddSavedPostAsync(SavedPost savedPost, CancellationToken cancellationToken) => Task.CompletedTask;
        public void RemoveSavedPost(SavedPost savedPost) { }
        public Task AddReportAsync(Report report, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PostCommentsResponse> GetPostCommentsAsync(GetPostCommentsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommentRepliesResponse> GetCommentRepliesAsync(GetCommentRepliesQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<VideoCommentsResponse> GetVideoCommentsAsync(GetVideoCommentsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<VideoRepliesResponse> GetVideoRepliesAsync(GetVideoRepliesQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
