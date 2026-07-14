using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class PostInteractionRepository(AppDbContext dbContext) : IPostInteractionRepository
{
    public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .Include(user => user.Account)
            .SingleOrDefaultAsync(user =>
                user.Id == userId &&
                user.Account.Status == AccountStatus.Active &&
                user.Account.DeletedAt == null,
                cancellationToken);

    public Task<Post?> GetPostForInteractionAsync(Guid postId, CancellationToken cancellationToken) =>
        dbContext.Posts
            .Include(post => post.User)
            .SingleOrDefaultAsync(post => post.Id == postId, cancellationToken);

    public Task<Post?> GetPostWithOriginalAsync(Guid postId, CancellationToken cancellationToken) =>
        dbContext.Posts
            .Include(post => post.OriginalPost)
            .SingleOrDefaultAsync(post => post.Id == postId, cancellationToken);

    public Task<Comment?> GetCommentForReplyAsync(Guid commentId, CancellationToken cancellationToken) =>
        dbContext.Comments
            .Include(comment => comment.User)
            .Include(comment => comment.Post)
            .SingleOrDefaultAsync(comment =>
                comment.Id == commentId &&
                comment.Status == CommentStatus.Published &&
                comment.DeletedAt == null,
                cancellationToken);

    public Task<PostReaction?> GetReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.PostReactions.SingleOrDefaultAsync(
            reaction => reaction.PostId == postId && reaction.UserId == userId,
            cancellationToken);

    public Task<SavedPost?> GetSavedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.SavedPosts.SingleOrDefaultAsync(
            saved => saved.PostId == postId && saved.UserId == userId,
            cancellationToken);

    public Task<bool> HasReportedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Reports.AnyAsync(report =>
            report.TargetId == postId &&
            report.TargetType == ReportTargetType.Post &&
            report.ReporterUserId == userId,
            cancellationToken);

    public Task AddReactionAsync(PostReaction reaction, CancellationToken cancellationToken) =>
        dbContext.PostReactions.AddAsync(reaction, cancellationToken).AsTask();

    public void RemoveReaction(PostReaction reaction) => dbContext.PostReactions.Remove(reaction);

    public Task AddCommentAsync(Comment comment, CancellationToken cancellationToken) =>
        dbContext.Comments.AddAsync(comment, cancellationToken).AsTask();

    public Task AddSavedPostAsync(SavedPost savedPost, CancellationToken cancellationToken) =>
        dbContext.SavedPosts.AddAsync(savedPost, cancellationToken).AsTask();

    public void RemoveSavedPost(SavedPost savedPost) => dbContext.SavedPosts.Remove(savedPost);

    public Task AddReportAsync(Report report, CancellationToken cancellationToken) =>
        dbContext.Reports.AddAsync(report, cancellationToken).AsTask();

    public Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken) =>
        dbContext.Notifications.AddAsync(notification, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> CanViewPostAsync(Post post, Guid userId, CancellationToken cancellationToken)
    {
        if (post.Visibility == PostVisibility.Public || post.UserId == userId)
        {
            return Task.FromResult(true);
        }

        if (post.Visibility == PostVisibility.Private)
        {
            return Task.FromResult(false);
        }

        return dbContext.Follows.AnyAsync(
            follow => follow.FollowerId == userId && follow.FollowingId == post.UserId,
            cancellationToken);
    }
}
