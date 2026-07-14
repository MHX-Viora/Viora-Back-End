using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed record GetCommunityPostsQuery(
    int Page,
    int PageSize,
    string? Keyword,
    Guid? ViewerUserId) : IRequest<PostFeedResponse>;

public sealed record PostFeedResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<PostFeedItemResponse> Items);

public sealed record PostFeedItemResponse(
    Guid Id,
    string? Content,
    PostType PostType,
    PostVisibility Visibility,
    string? Location,
    DateTime CreatedAt,
    PostFeedUserResponse User,
    IReadOnlyList<PostFeedMediaResponse> Media,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    bool IsReacted,
    ReactionType? ReactionType,
    bool IsSaved);

public sealed record PostFeedUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record PostFeedMediaResponse(
    Guid Id,
    string MediaUrl,
    string? ThumbnailUrl);

public interface IPostFeedRepository
{
    Task<PostFeedResponse> GetCommunityPostsAsync(
        GetCommunityPostsQuery query,
        CancellationToken cancellationToken);
}
