using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class ProfileFeedRepository(AppDbContext db) : IProfileFeedRepository
{
    public async Task<PostFeedResponse> GetProfileFeedAsync(GetProfileFeedQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var userExists = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == query.UserId &&
            user.Account.Status == AccountStatus.Active &&
            user.Account.DeletedAt == null,
            cancellationToken);
        if (!userExists)
        {
            return new PostFeedResponse(page, pageSize, 0, 0, []);
        }

        var postType = query.Kind is ProfileFeedKind.ReactedReels or ProfileFeedKind.SavedReels
            ? PostType.ShortVideo
            : PostType.Post;

        return query.Kind is ProfileFeedKind.ReactedPosts or ProfileFeedKind.ReactedReels
            ? await GetReactedAsync(query.UserId, postType, page, pageSize, cancellationToken)
            : await GetSavedAsync(query.UserId, postType, page, pageSize, cancellationToken);
    }

    private async Task<PostFeedResponse> GetReactedAsync(Guid userId, PostType postType, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PostReactions.AsNoTracking()
            .Where(reaction => reaction.UserId == userId)
            .Where(reaction =>
                reaction.Post.PostType == postType &&
                reaction.Post.Status == PostStatus.Published &&
                reaction.Post.DeletedAt == null &&
                (reaction.Post.UserId == userId ||
                 reaction.Post.Visibility == PostVisibility.Public ||
                 (reaction.Post.Visibility == PostVisibility.Followers &&
                  db.Follows.Any(follow => follow.FollowerId == userId && follow.FollowingId == reaction.Post.UserId))));

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = await query
            .OrderByDescending(reaction => reaction.CreatedAt)
            .ThenByDescending(reaction => reaction.PostId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(reaction => new PostFeedItemResponse(
                reaction.Post.Id,
                reaction.Post.Content,
                reaction.Post.PostType,
                reaction.Post.Visibility,
                reaction.Post.Location,
                string.IsNullOrWhiteSpace(reaction.Post.Link) ? null : reaction.Post.Link,
                reaction.Post.CreatedAt,
                new PostFeedUserResponse(
                    reaction.Post.User.Id,
                    reaction.Post.User.DisplayName,
                    reaction.Post.User.AvatarUrl,
                    reaction.Post.User.IsVerified),
                reaction.Post.Media
                    .OrderBy(media => media.CreatedAt)
                    .Select(media => new PostFeedMediaResponse(media.Id, media.MediaUrl, media.ThumbnailUrl))
                    .ToList(),
                reaction.Post.ReactionCount,
                reaction.Post.CommentCount,
                reaction.Post.ShareCount,
                reaction.Post.SaveCount,
                reaction.Post.ViewCount,
                reaction.Post.UserId == userId,
                true,
                (ReactionType?)reaction.ReactionType,
                db.SavedPosts.Any(saved => saved.PostId == reaction.Post.Id && saved.UserId == userId),
                db.PostHashtags
                    .Where(postHashtag => postHashtag.PostId == reaction.Post.Id)
                    .OrderBy(postHashtag => postHashtag.Hashtag.Name)
                    .Select(postHashtag => new PostDetailHashtagResponse(postHashtag.Hashtag.Id, postHashtag.Hashtag.Name))
                    .ToList(),
                null))
            .ToListAsync(cancellationToken);

        return new PostFeedResponse(page, pageSize, totalItems, totalPages, items);
    }

    private async Task<PostFeedResponse> GetSavedAsync(Guid userId, PostType postType, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.SavedPosts.AsNoTracking()
            .Where(saved => saved.UserId == userId)
            .Where(saved =>
                saved.Post.PostType == postType &&
                saved.Post.Status == PostStatus.Published &&
                saved.Post.DeletedAt == null &&
                (saved.Post.UserId == userId ||
                 saved.Post.Visibility == PostVisibility.Public ||
                 (saved.Post.Visibility == PostVisibility.Followers &&
                  db.Follows.Any(follow => follow.FollowerId == userId && follow.FollowingId == saved.Post.UserId))));

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = await query
            .OrderByDescending(saved => saved.CreatedAt)
            .ThenByDescending(saved => saved.PostId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(saved => new PostFeedItemResponse(
                saved.Post.Id,
                saved.Post.Content,
                saved.Post.PostType,
                saved.Post.Visibility,
                saved.Post.Location,
                string.IsNullOrWhiteSpace(saved.Post.Link) ? null : saved.Post.Link,
                saved.Post.CreatedAt,
                new PostFeedUserResponse(
                    saved.Post.User.Id,
                    saved.Post.User.DisplayName,
                    saved.Post.User.AvatarUrl,
                    saved.Post.User.IsVerified),
                saved.Post.Media
                    .OrderBy(media => media.CreatedAt)
                    .Select(media => new PostFeedMediaResponse(media.Id, media.MediaUrl, media.ThumbnailUrl))
                    .ToList(),
                saved.Post.ReactionCount,
                saved.Post.CommentCount,
                saved.Post.ShareCount,
                saved.Post.SaveCount,
                saved.Post.ViewCount,
                saved.Post.UserId == userId,
                db.PostReactions.Any(reaction => reaction.PostId == saved.Post.Id && reaction.UserId == userId),
                db.PostReactions
                    .Where(reaction => reaction.PostId == saved.Post.Id && reaction.UserId == userId)
                    .Select(reaction => (ReactionType?)reaction.ReactionType)
                    .FirstOrDefault(),
                true,
                db.PostHashtags
                    .Where(postHashtag => postHashtag.PostId == saved.Post.Id)
                    .OrderBy(postHashtag => postHashtag.Hashtag.Name)
                    .Select(postHashtag => new PostDetailHashtagResponse(postHashtag.Hashtag.Id, postHashtag.Hashtag.Name))
                    .ToList(),
                null))
            .ToListAsync(cancellationToken);

        return new PostFeedResponse(page, pageSize, totalItems, totalPages, items);
    }
}
