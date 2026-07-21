using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed record GetCommunityPostsQuery(
    int Page,
    int PageSize,
    string? Keyword,
    Guid? UserId,
    Guid? ViewerUserId) : IRequest<PostFeedResponse>;

public sealed record GetPostDetailQuery(Guid UserId, Guid PostId)
    : IRequest<Result<PostDetailResponse>>;

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
    string? Link,
    DateTime CreatedAt,
    PostFeedUserResponse User,
    IReadOnlyList<PostFeedMediaResponse> Media,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    bool IsMine,
    bool IsReacted,
    ReactionType? ReactionType,
    bool IsSaved,
    IReadOnlyList<PostDetailHashtagResponse> Hashtags,
    PostFeedOriginalPostResponse? OriginalPost);

public sealed record PostFeedOriginalPostResponse(
    Guid Id,
    string? Content,
    PostType PostType,
    PostVisibility Visibility,
    string? Location,
    string? Link,
    DateTime CreatedAt,
    PostFeedUserResponse User,
    IReadOnlyList<PostFeedMediaResponse> Media,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount);

public sealed record PostFeedUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record PostFeedMediaResponse(
    Guid Id,
    string MediaUrl,
    string? ThumbnailUrl);

public enum PostDetailMediaType : short
{
    Image = 0,
    Video = 1
}

public sealed record PostDetailResponse(
    Guid Id,
    PostType PostType,
    string? Content,
    PostVisibility Visibility,
    string? Location,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    ReactionType? MyReaction,
    bool IsSaved,
    bool IsOwner,
    PostDetailUserResponse User,
    IReadOnlyList<PostDetailMediaResponse> Media,
    IReadOnlyList<PostDetailHashtagResponse> Hashtags);

public sealed record PostDetailUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record PostDetailMediaResponse(
    Guid Id,
    PostDetailMediaType MediaType,
    string MediaUrl,
    string? ThumbnailUrl);

public sealed record PostDetailHashtagResponse(Guid Id, string Name);

public interface IPostFeedRepository
{
    Task<PostFeedResponse> GetCommunityPostsAsync(
        GetCommunityPostsQuery query,
        CancellationToken cancellationToken);

    Task<Result<PostDetailResponse>> GetPostDetailAsync(
        GetPostDetailQuery query,
        CancellationToken cancellationToken);
}
