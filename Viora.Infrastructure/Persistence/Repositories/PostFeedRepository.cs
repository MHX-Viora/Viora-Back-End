using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class PostFeedRepository(AppDbContext dbContext) : IPostFeedRepository
{
    public async Task<PostFeedResponse> GetCommunityPostsAsync(
        GetCommunityPostsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var viewerUserId = query.ViewerUserId;

        var posts = dbContext.Posts
            .AsNoTracking()
            .Where(post =>
                post.PostType == PostType.Post &&
                post.Status == PostStatus.Published &&
                post.DeletedAt == null &&
                (post.Visibility == PostVisibility.Public ||
                    (viewerUserId.HasValue && post.UserId == viewerUserId.Value) ||
                    (viewerUserId.HasValue &&
                        post.Visibility == PostVisibility.Followers &&
                        dbContext.Follows.Any(follow =>
                            follow.FollowerId == viewerUserId.Value &&
                            follow.FollowingId == post.UserId))));

        if (query.UserId.HasValue)
        {
            posts = posts.Where(post => post.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = $"%{query.Keyword.Trim()}%";
            posts = posts.Where(post =>
                EF.Functions.ILike(post.User.DisplayName, keyword) ||
                (post.Content != null && EF.Functions.ILike(post.Content, keyword)) ||
                (post.Location != null && EF.Functions.ILike(post.Location, keyword)));
        }

        var totalItems = await posts.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var ranked = posts.Select(post => new
        {
            Post = post,
            IsReacted = viewerUserId.HasValue && dbContext.PostReactions.Any(reaction =>
                reaction.UserId == viewerUserId.Value &&
                reaction.PostId == post.Id),
            ReactionType = viewerUserId.HasValue
                ? dbContext.PostReactions
                    .Where(reaction => reaction.UserId == viewerUserId.Value && reaction.PostId == post.Id)
                    .Select(reaction => (ReactionType?)reaction.ReactionType)
                    .FirstOrDefault()
                : null,
            IsSaved = viewerUserId.HasValue && dbContext.SavedPosts.Any(saved =>
                saved.UserId == viewerUserId.Value &&
                saved.PostId == post.Id)
        });

        var items = await ranked
            .OrderByDescending(item => item.Post.CreatedAt)
            .ThenBy(item => item.Post.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(item => new PostFeedItemResponse(
                item.Post.Id,
                item.Post.Content,
                item.Post.PostType,
                item.Post.Visibility,
                item.Post.Location,
                string.IsNullOrWhiteSpace(item.Post.Link) ? null : item.Post.Link,
                item.Post.CreatedAt,
                new PostFeedUserResponse(
                    item.Post.User.Id,
                    item.Post.User.DisplayName,
                    item.Post.User.AvatarUrl,
                    item.Post.User.IsVerified),
                item.Post.Media
                    .OrderBy(media => media.CreatedAt)
                    .Select(media => new PostFeedMediaResponse(
                        media.Id,
                        media.MediaUrl,
                        media.ThumbnailUrl))
                    .ToList(),
                item.Post.ReactionCount,
                item.Post.CommentCount,
                item.Post.ShareCount,
                item.Post.SaveCount,
                item.Post.ViewCount,
                item.IsReacted,
                item.ReactionType,
                item.IsSaved,
                item.Post.OriginalPost == null
                    ? null
                    : new PostFeedOriginalPostResponse(
                        item.Post.OriginalPost.Id,
                        item.Post.OriginalPost.Content,
                        item.Post.OriginalPost.PostType,
                        item.Post.OriginalPost.Visibility,
                        item.Post.OriginalPost.Location,
                        string.IsNullOrWhiteSpace(item.Post.OriginalPost.Link) ? null : item.Post.OriginalPost.Link,
                        item.Post.OriginalPost.CreatedAt,
                        new PostFeedUserResponse(
                            item.Post.OriginalPost.User.Id,
                            item.Post.OriginalPost.User.DisplayName,
                            item.Post.OriginalPost.User.AvatarUrl,
                            item.Post.OriginalPost.User.IsVerified),
                        item.Post.OriginalPost.Media
                            .OrderBy(media => media.CreatedAt)
                            .Select(media => new PostFeedMediaResponse(
                                media.Id,
                                media.MediaUrl,
                                media.ThumbnailUrl))
                            .ToList(),
                        item.Post.OriginalPost.ReactionCount,
                        item.Post.OriginalPost.CommentCount,
                        item.Post.OriginalPost.ShareCount,
                        item.Post.OriginalPost.SaveCount,
                        item.Post.OriginalPost.ViewCount)))
            .ToListAsync(cancellationToken);

        return new PostFeedResponse(page, pageSize, totalItems, totalPages, items);
    }
}
