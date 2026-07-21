using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class PostFeedRepository(AppDbContext dbContext) : IPostFeedRepository
{
    public async Task<Result<PostDetailResponse>> GetPostDetailAsync(
        GetPostDetailQuery query,
        CancellationToken cancellationToken)
    {
        var access = await dbContext.Posts
            .AsNoTracking()
            .Where(post => post.Id == query.PostId)
            .Select(post => new
            {
                post.UserId,
                post.Status,
                post.Visibility,
                post.DeletedAt,
                IsFollower = dbContext.Follows.Any(follow =>
                    follow.FollowerId == query.UserId &&
                    follow.FollowingId == post.UserId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (access is null || access.Status == PostStatus.Deleted || access.DeletedAt is not null)
        {
            return Result<PostDetailResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay bai viet.");
        }

        var isOwner = access.UserId == query.UserId;
        var canView = access.Status == PostStatus.Published &&
            (isOwner || access.Visibility == PostVisibility.Public ||
                (access.Visibility == PostVisibility.Followers && access.IsFollower));

        if (!canView)
        {
            return Result<PostDetailResponse>.Failure(PostInteractionError.Forbidden, "Ban khong co quyen xem bai viet nay.");
        }

        var response = await dbContext.Posts
            .AsNoTracking()
            .Where(post => post.Id == query.PostId && post.Status == PostStatus.Published && post.DeletedAt == null)
            .Select(post => new PostDetailResponse(
                post.Id,
                post.PostType,
                post.Content,
                post.Visibility,
                post.Location,
                post.CreatedAt,
                post.UpdatedAt,
                post.ReactionCount,
                post.CommentCount,
                post.ShareCount,
                post.SaveCount,
                post.ViewCount,
                dbContext.PostReactions
                    .Where(reaction => reaction.PostId == post.Id && reaction.UserId == query.UserId)
                    .Select(reaction => (ReactionType?)reaction.ReactionType)
                    .FirstOrDefault(),
                dbContext.SavedPosts.Any(saved => saved.PostId == post.Id && saved.UserId == query.UserId),
                post.UserId == query.UserId,
                new PostDetailUserResponse(
                    post.User.Id,
                    post.User.DisplayName,
                    post.User.AvatarUrl,
                    post.User.IsVerified),
                post.Media
                    .OrderBy(media => media.CreatedAt)
                    .Select(media => new PostDetailMediaResponse(
                        media.Id,
                        post.PostType == PostType.ShortVideo
                            ? PostDetailMediaType.Video
                            : PostDetailMediaType.Image,
                        media.MediaUrl,
                        media.ThumbnailUrl))
                    .ToList(),
                dbContext.PostHashtags
                    .Where(postHashtag => postHashtag.PostId == post.Id)
                    .OrderBy(postHashtag => postHashtag.Hashtag.Name)
                    .Select(postHashtag => new PostDetailHashtagResponse(
                        postHashtag.Hashtag.Id,
                        postHashtag.Hashtag.Name))
                    .ToList()))
            .SingleAsync(cancellationToken);

        return Result<PostDetailResponse>.Success(response);
    }

    public async Task<PostFeedResponse> GetCommunityPostsAsync(
        GetCommunityPostsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var viewerUserId = query.ViewerUserId;
        var now = DateTime.UtcNow;
        var oneDayAgo = now.AddDays(-1);
        var threeDaysAgo = now.AddDays(-3);
        var sevenDaysAgo = now.AddDays(-7);
        var hasBehavior = viewerUserId.HasValue && await HasBehaviorAsync(viewerUserId.Value, cancellationToken);

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
            IsFollowed = viewerUserId.HasValue && dbContext.Follows.Any(follow =>
                follow.FollowerId == viewerUserId.Value &&
                follow.FollowingId == post.UserId),
            IsFriend = viewerUserId.HasValue && dbContext.Friendships.Any(friendship =>
                friendship.Status == FriendshipStatus.Accepted &&
                ((friendship.RequesterUserId == viewerUserId.Value && friendship.AddresseeUserId == post.UserId) ||
                    (friendship.AddresseeUserId == viewerUserId.Value && friendship.RequesterUserId == post.UserId))),
            HasViewed = viewerUserId.HasValue && dbContext.ViewHistories.Any(view =>
                view.UserId == viewerUserId.Value &&
                view.PostId == post.Id),
            HasInterestedHashtag = viewerUserId.HasValue && dbContext.PostHashtags.Any(postTag =>
                postTag.PostId == post.Id &&
                dbContext.PostHashtags.Any(historyTag =>
                    historyTag.HashtagId == postTag.HashtagId &&
                    (
                        dbContext.ViewHistories.Any(view =>
                            view.UserId == viewerUserId.Value &&
                            view.PostId == historyTag.PostId) ||
                        dbContext.PostReactions.Any(reaction =>
                            reaction.UserId == viewerUserId.Value &&
                            reaction.PostId == historyTag.PostId) ||
                        dbContext.SavedPosts.Any(saved =>
                            saved.UserId == viewerUserId.Value &&
                            saved.PostId == historyTag.PostId)
                    ))),
            PopularHashtagScore = dbContext.PostHashtags
                .Where(postHashtag => postHashtag.PostId == post.Id)
                .Select(postHashtag => (int?)postHashtag.Hashtag.PostCount)
                .Max() ?? 0,
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

        var ordered = ranked
            .OrderByDescending(item => hasBehavior
                ? (item.IsFriend ? 1300 : 0) +
                    (item.IsFollowed ? 900 : 0) +
                    (item.HasInterestedHashtag ? 600 : 0) +
                    (item.Post.CreatedAt >= oneDayAgo ? 300 :
                        item.Post.CreatedAt >= threeDaysAgo ? 180 :
                        item.Post.CreatedAt >= sevenDaysAgo ? 80 : 20) +
                    item.Post.ReactionCount * 4 +
                    item.Post.CommentCount * 6 +
                    item.Post.ShareCount * 8 +
                    item.Post.SaveCount * 5 +
                    item.Post.ViewCount -
                    (item.HasViewed ? 250 : 0)
                : item.PopularHashtagScore * 3 +
                    item.Post.ReactionCount * 5 +
                    item.Post.CommentCount * 7 +
                    item.Post.ShareCount * 9 +
                    item.Post.SaveCount * 6 +
                    item.Post.ViewCount +
                    (item.Post.CreatedAt >= oneDayAgo ? 180 :
                        item.Post.CreatedAt >= threeDaysAgo ? 110 :
                        item.Post.CreatedAt >= sevenDaysAgo ? 55 : 10))
            .ThenByDescending(item => item.Post.ReactionCount + item.Post.CommentCount + item.Post.ShareCount + item.Post.SaveCount)
            .ThenByDescending(item => item.PopularHashtagScore)
            .ThenByDescending(item => item.Post.CreatedAt)
            .ThenBy(item => item.Post.Id);

        var items = await ordered
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
                viewerUserId.HasValue && item.Post.UserId == viewerUserId.Value,
                item.IsReacted,
                item.ReactionType,
                item.IsSaved,
                dbContext.PostHashtags
                    .Where(postHashtag => postHashtag.PostId == item.Post.Id)
                    .OrderBy(postHashtag => postHashtag.Hashtag.Name)
                    .Select(postHashtag => new PostDetailHashtagResponse(
                        postHashtag.Hashtag.Id,
                        postHashtag.Hashtag.Name))
                    .ToList(),
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

    private async Task<bool> HasBehaviorAsync(Guid viewerUserId, CancellationToken cancellationToken) =>
        await dbContext.Follows.AsNoTracking().AnyAsync(follow => follow.FollowerId == viewerUserId, cancellationToken) ||
        await dbContext.ViewHistories.AsNoTracking().AnyAsync(view => view.UserId == viewerUserId, cancellationToken) ||
        await dbContext.PostReactions.AsNoTracking().AnyAsync(reaction => reaction.UserId == viewerUserId, cancellationToken) ||
        await dbContext.SavedPosts.AsNoTracking().AnyAsync(saved => saved.UserId == viewerUserId, cancellationToken);
}
