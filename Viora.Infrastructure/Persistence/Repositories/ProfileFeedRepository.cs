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
        var skip = (page - 1) * pageSize;
        var postType = query.Kind is ProfileFeedKind.ReactedReels or ProfileFeedKind.SavedReels
            ? PostType.ShortVideo
            : PostType.Post;

        var posts = query.Kind is ProfileFeedKind.ReactedPosts or ProfileFeedKind.ReactedReels
            ? db.PostReactions.AsNoTracking()
                .Where(reaction => reaction.UserId == query.UserId)
                .Where(reaction =>
                    reaction.Post.PostType == postType &&
                    reaction.Post.Status == PostStatus.Published &&
                    reaction.Post.DeletedAt == null &&
                    (reaction.Post.UserId == query.UserId ||
                     reaction.Post.Visibility == PostVisibility.Public ||
                     (reaction.Post.Visibility == PostVisibility.Followers &&
                      db.Follows.Any(follow => follow.FollowerId == query.UserId && follow.FollowingId == reaction.Post.UserId))))
                .Select(reaction => new ProfileFeedRow(reaction.Post, reaction.CreatedAt))
            : db.SavedPosts.AsNoTracking()
                .Where(saved => saved.UserId == query.UserId)
                .Where(saved =>
                    saved.Post.PostType == postType &&
                    saved.Post.Status == PostStatus.Published &&
                    saved.Post.DeletedAt == null &&
                    (saved.Post.UserId == query.UserId ||
                     saved.Post.Visibility == PostVisibility.Public ||
                     (saved.Post.Visibility == PostVisibility.Followers &&
                      db.Follows.Any(follow => follow.FollowerId == query.UserId && follow.FollowingId == saved.Post.UserId))))
                .Select(saved => new ProfileFeedRow(saved.Post, saved.CreatedAt));

        var totalItems = await posts.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await posts
            .OrderByDescending(row => row.SortAt)
            .ThenByDescending(row => row.Post.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(row => new PostFeedItemResponse(
                row.Post.Id,
                row.Post.Content,
                row.Post.PostType,
                row.Post.Visibility,
                row.Post.Location,
                string.IsNullOrWhiteSpace(row.Post.Link) ? null : row.Post.Link,
                row.Post.CreatedAt,
                new PostFeedUserResponse(
                    row.Post.User.Id,
                    row.Post.User.DisplayName,
                    row.Post.User.AvatarUrl,
                    row.Post.User.IsVerified),
                row.Post.Media
                    .OrderBy(media => media.CreatedAt)
                    .Select(media => new PostFeedMediaResponse(media.Id, media.MediaUrl, media.ThumbnailUrl))
                    .ToList(),
                row.Post.ReactionCount,
                row.Post.CommentCount,
                row.Post.ShareCount,
                row.Post.SaveCount,
                row.Post.ViewCount,
                row.Post.UserId == query.UserId,
                db.PostReactions.Any(reaction => reaction.PostId == row.Post.Id && reaction.UserId == query.UserId),
                db.PostReactions
                    .Where(reaction => reaction.PostId == row.Post.Id && reaction.UserId == query.UserId)
                    .Select(reaction => (ReactionType?)reaction.ReactionType)
                    .FirstOrDefault(),
                db.SavedPosts.Any(saved => saved.PostId == row.Post.Id && saved.UserId == query.UserId),
                db.PostHashtags
                    .Where(postHashtag => postHashtag.PostId == row.Post.Id)
                    .OrderBy(postHashtag => postHashtag.Hashtag.Name)
                    .Select(postHashtag => new PostDetailHashtagResponse(postHashtag.Hashtag.Id, postHashtag.Hashtag.Name))
                    .ToList(),
                null))
            .ToListAsync(cancellationToken);

        return new PostFeedResponse(page, pageSize, totalItems, totalPages, items);
    }

    private sealed record ProfileFeedRow(Post Post, DateTime SortAt);
}
