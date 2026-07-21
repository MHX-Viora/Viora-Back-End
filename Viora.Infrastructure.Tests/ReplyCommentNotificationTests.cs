using Viora.Application.Notifications;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ReplyCommentNotificationTests
{
    [Fact]
    public async Task Reply_comment_notification_uses_post_id_as_reference_id()
    {
        var postId = Guid.NewGuid();
        var parentCommentId = Guid.NewGuid();
        var parentOwnerId = Guid.NewGuid();
        var replier = new User { Id = Guid.NewGuid(), DisplayName = "Nam" };
        var parentOwner = new User { Id = parentOwnerId, DisplayName = "Lan" };
        var post = new Post { Id = postId, UserId = parentOwnerId, PostType = PostType.Post, Status = PostStatus.Published };
        var parent = new Comment
        {
            Id = parentCommentId,
            PostId = postId,
            Post = post,
            UserId = parentOwnerId,
            User = parentOwner,
            Content = "parent",
            Status = CommentStatus.Published
        };
        var repository = new ReplyNotificationRepository(replier, parent);
        var notificationService = new CaptureNotificationService();
        var handler = new ReplyCommentHandler(repository, new ReplyCommentValidator(), notificationService);

        var result = await handler.Handle(new ReplyCommentCommand(replier.Id, parentCommentId, "reply"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Notification);
        Assert.Equal(postId, repository.Notification!.ReferenceId);
        Assert.NotEqual(parentCommentId, repository.Notification.ReferenceId);
        Assert.Equal(NotificationReferenceType.Comment, repository.Notification.ReferenceType);
    }

    private sealed class CaptureNotificationService : INotificationService
    {
        public Task<Notification> SendAsync(SendNotificationCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PublishAsync(Notification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ReplyNotificationRepository(User replier, Comment parent) : IPostInteractionRepository
    {
        public Notification? Notification { get; private set; }

        public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(userId == replier.Id ? replier : null);

        public Task<Comment?> GetCommentForReplyAsync(Guid commentId, CancellationToken cancellationToken) =>
            Task.FromResult<Comment?>(commentId == parent.Id ? parent : null);

        public Task AddCommentAsync(Comment comment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken)
        {
            Notification = notification;
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<bool> CanViewPostAsync(Post post, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Post?> GetPostForInteractionAsync(Guid postId, CancellationToken cancellationToken) => Task.FromResult<Post?>(null);
        public Task<Post?> GetPostWithOriginalAsync(Guid postId, CancellationToken cancellationToken) => Task.FromResult<Post?>(null);
        public Task<Comment?> GetCommentForDeleteAsync(Guid commentId, CancellationToken cancellationToken) => Task.FromResult<Comment?>(null);
        public Task<PostReaction?> GetReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<PostReaction?>(null);
        public Task<SavedPost?> GetSavedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<SavedPost?>(null);
        public Task<bool> HasReportedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddReactionAsync(PostReaction reaction, CancellationToken cancellationToken) => Task.CompletedTask;
        public void RemoveReaction(PostReaction reaction) { }
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
