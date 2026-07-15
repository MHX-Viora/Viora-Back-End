using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed record GetShortVideosQuery(
    int Page,
    int PageSize,
    string Sort,
    string? Keyword,
    Guid? UserId,
    Guid ViewerUserId) : IRequest<Result<VideoFeedResponse>>;

public sealed record VideoFeedResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<VideoFeedItemResponse> Items);

public sealed record VideoFeedItemResponse(
    Guid Id,
    string? Content,
    string? Location,
    DateTime CreatedAt,
    int ViewCount,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    bool IsSaved,
    bool IsReacted,
    ReactionType? ReactionType,
    IReadOnlyList<VideoFeedMediaResponse> Media,
    IReadOnlyList<string> Hashtags,
    VideoFeedUserResponse User);

public sealed record VideoFeedMediaResponse(
    Guid Id,
    string MediaUrl,
    string? ThumbnailUrl);

public sealed record VideoFeedUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified,
    bool IsFollowing);

public sealed class GetShortVideosValidator : AbstractValidator<GetShortVideosQuery>
{
    private static readonly string[] Sorts = ["recommend", "following", "friends", "latest", "popular"];

    public GetShortVideosValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Sort)
            .NotEmpty()
            .Must(sort => Sorts.Contains(sort.Trim().ToLowerInvariant()))
            .WithMessage("Sort must be recommend, following, friends, latest, or popular.");
        RuleFor(query => query.Keyword).MaximumLength(255);
    }
}

public interface IVideoFeedRepository
{
    Task<VideoFeedResponse> GetShortVideosAsync(
        GetShortVideosQuery query,
        CancellationToken cancellationToken);
}
