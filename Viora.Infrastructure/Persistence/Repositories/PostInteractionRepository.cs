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

    public Task<Comment?> GetCommentForLikeAsync(Guid commentId, CancellationToken cancellationToken) =>
        dbContext.Comments
            .Include(comment => comment.User)
            .SingleOrDefaultAsync(comment => comment.Id == commentId, cancellationToken);

    public Task<Comment?> GetCommentForDeleteAsync(Guid commentId, CancellationToken cancellationToken) =>
        dbContext.Comments
            .Include(comment => comment.Post)
            .Include(comment => comment.ParentComment)
            .Include(comment => comment.Replies)
            .SingleOrDefaultAsync(comment =>
                comment.Id == commentId &&
                comment.Status == CommentStatus.Published &&
                comment.DeletedAt == null,
                cancellationToken);

    public Task<PostReaction?> GetReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.PostReactions.SingleOrDefaultAsync(
            reaction => reaction.PostId == postId && reaction.UserId == userId,
            cancellationToken);

    public Task<CommentReaction?> GetCommentReactionAsync(Guid commentId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.CommentReactions.SingleOrDefaultAsync(
            reaction => reaction.CommentId == commentId && reaction.UserId == userId,
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

    public Task AddCommentReactionAsync(CommentReaction reaction, CancellationToken cancellationToken) =>
        dbContext.CommentReactions.AddAsync(reaction, cancellationToken).AsTask();

    public void RemoveCommentReaction(CommentReaction reaction) => dbContext.CommentReactions.Remove(reaction);

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

    public async Task<PostCommentsResponse> GetPostCommentsAsync(
        GetPostCommentsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var comments = dbContext.Comments
            .AsNoTracking()
            .Where(comment =>
                comment.PostId == query.PostId &&
                comment.ParentCommentId == null &&
                comment.Status == CommentStatus.Published &&
                comment.DeletedAt == null);

        var totalItems = await comments.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        comments = string.Equals(query.Sort, "oldest", StringComparison.OrdinalIgnoreCase)
            ? comments.OrderBy(comment => comment.CreatedAt).ThenBy(comment => comment.Id)
            : comments.OrderByDescending(comment => comment.CreatedAt).ThenBy(comment => comment.Id);

        var items = await comments
            .Skip(skip)
            .Take(pageSize)
            .Select(comment => new PostCommentListItemResponse(
                comment.Id,
                comment.Content,
                comment.CreatedAt,
                comment.UpdatedAt,
                comment.LikeCount,
                comment.ReplyCount,
                dbContext.CommentReactions.Any(reaction =>
                    reaction.CommentId == comment.Id &&
                    reaction.UserId == query.UserId),
                new PostInteractionUserResponse(
                    comment.User.Id,
                    comment.User.DisplayName,
                    comment.User.AvatarUrl,
                    comment.User.IsVerified)))
            .ToListAsync(cancellationToken);

        return new PostCommentsResponse(page, pageSize, totalItems, totalPages, items);
    }

    public async Task<CommentRepliesResponse> GetCommentRepliesAsync(
        GetCommentRepliesQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var replies = dbContext.Comments
            .AsNoTracking()
            .Where(comment =>
                comment.ParentCommentId == query.CommentId &&
                comment.Status == CommentStatus.Published &&
                comment.DeletedAt == null);

        var totalItems = await replies.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        replies = string.Equals(query.Sort, "newest", StringComparison.OrdinalIgnoreCase)
            ? replies.OrderByDescending(comment => comment.CreatedAt).ThenBy(comment => comment.Id)
            : replies.OrderBy(comment => comment.CreatedAt).ThenBy(comment => comment.Id);

        var items = await replies
            .Skip(skip)
            .Take(pageSize)
            .Select(comment => new CommentReplyListItemResponse(
                comment.Id,
                comment.Content,
                comment.CreatedAt,
                comment.UpdatedAt,
                comment.LikeCount,
                dbContext.CommentReactions.Any(reaction =>
                    reaction.CommentId == comment.Id &&
                    reaction.UserId == query.UserId),
                comment.ReplyToUser == null
                    ? null
                    : new CommentReplyToUserResponse(
                        comment.ReplyToUser.Id,
                        comment.ReplyToUser.DisplayName),
                new PostInteractionUserResponse(
                    comment.User.Id,
                    comment.User.DisplayName,
                    comment.User.AvatarUrl,
                    comment.User.IsVerified)))
            .ToListAsync(cancellationToken);

        return new CommentRepliesResponse(page, pageSize, totalItems, totalPages, items);
    }

    public async Task<VideoCommentsResponse> GetVideoCommentsAsync(
        GetVideoCommentsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var comments = dbContext.Comments
            .AsNoTracking()
            .Where(comment =>
                comment.PostId == query.VideoId &&
                comment.ParentCommentId == null &&
                comment.Status == CommentStatus.Published &&
                comment.DeletedAt == null);

        var totalItems = await comments.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await comments
            .OrderByDescending(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(comment => new VideoCommentListItemResponse(
                comment.Id,
                comment.Content,
                comment.CreatedAt,
                comment.LikeCount,
                comment.ReplyCount,
                dbContext.CommentReactions.Any(reaction =>
                    reaction.CommentId == comment.Id &&
                    reaction.UserId == query.UserId),
                new PostInteractionUserResponse(
                    comment.User.Id,
                    comment.User.DisplayName,
                    comment.User.AvatarUrl,
                    comment.User.IsVerified)))
            .ToListAsync(cancellationToken);

        return new VideoCommentsResponse(page, pageSize, totalItems, totalPages, items);
    }

    public async Task<VideoRepliesResponse> GetVideoRepliesAsync(
        GetVideoRepliesQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var replies = dbContext.Comments
            .AsNoTracking()
            .Where(comment =>
                comment.ParentCommentId == query.CommentId &&
                comment.Status == CommentStatus.Published &&
                comment.DeletedAt == null);

        var totalItems = await replies.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await replies
            .OrderByDescending(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(comment => new VideoReplyListItemResponse(
                comment.Id,
                comment.Content,
                comment.CreatedAt,
                comment.LikeCount,
                dbContext.CommentReactions.Any(reaction =>
                    reaction.CommentId == comment.Id &&
                    reaction.UserId == query.UserId),
                new PostInteractionUserResponse(
                    comment.ReplyToUser!.Id,
                    comment.ReplyToUser.DisplayName,
                    comment.ReplyToUser.AvatarUrl,
                    comment.ReplyToUser.IsVerified),
                new PostInteractionUserResponse(
                    comment.User.Id,
                    comment.User.DisplayName,
                    comment.User.AvatarUrl,
                    comment.User.IsVerified)))
            .ToListAsync(cancellationToken);

        return new VideoRepliesResponse(page, pageSize, totalItems, totalPages, items);
    }
}
