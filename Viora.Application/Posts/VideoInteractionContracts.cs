using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed record ToggleVideoReactionCommand(Guid UserId, Guid VideoId)
    : IRequest<Result<VideoReactionResponse>>;

public sealed record ToggleVideoSaveCommand(Guid UserId, Guid VideoId)
    : IRequest<Result<SavePostResponse>>;

public sealed record ShareVideoCommand(Guid UserId, Guid VideoId)
    : IRequest<Result<VideoShareResponse>>;

public sealed record CreateVideoCommentCommand(Guid UserId, Guid VideoId, string Content)
    : IRequest<Result<VideoCommentResponse>>;

public sealed record ReplyVideoCommentCommand(Guid UserId, Guid CommentId, string Content)
    : IRequest<Result<VideoReplyResponse>>;

public sealed record GetVideoCommentsQuery(Guid UserId, Guid VideoId, int Page, int PageSize)
    : IRequest<Result<VideoCommentsResponse>>;

public sealed record GetVideoRepliesQuery(Guid UserId, Guid CommentId, int Page, int PageSize)
    : IRequest<Result<VideoRepliesResponse>>;

public sealed record DeleteVideoCommentCommand(Guid UserId, bool IsAdmin, Guid CommentId)
    : IRequest<Result<EmptyResponse>>;

public sealed record VideoReactionResponse(bool IsLiked, int ReactionCount);

public sealed record VideoShareResponse(int ShareCount);

public sealed record VideoCommentsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<VideoCommentListItemResponse> Items);

public sealed record VideoRepliesResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<VideoReplyListItemResponse> Items);

public sealed record VideoCommentResponse(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    int LikeCount,
    int ReplyCount,
    PostInteractionUserResponse User);

public sealed record VideoCommentListItemResponse(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    int LikeCount,
    int ReplyCount,
    bool IsLiked,
    PostInteractionUserResponse User);

public sealed record VideoReplyResponse(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    int LikeCount,
    int ReplyCount,
    PostInteractionUserResponse User);

public sealed record VideoReplyListItemResponse(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    int LikeCount,
    bool IsLiked,
    PostInteractionUserResponse ReplyToUser,
    PostInteractionUserResponse User);

public sealed class ToggleVideoReactionValidator : AbstractValidator<ToggleVideoReactionCommand>
{
    public ToggleVideoReactionValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.VideoId).NotEmpty();
    }
}

public sealed class ToggleVideoSaveValidator : AbstractValidator<ToggleVideoSaveCommand>
{
    public ToggleVideoSaveValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.VideoId).NotEmpty();
    }
}

public sealed class ShareVideoValidator : AbstractValidator<ShareVideoCommand>
{
    public ShareVideoValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.VideoId).NotEmpty();
    }
}

public sealed class CreateVideoCommentValidator : AbstractValidator<CreateVideoCommentCommand>
{
    public CreateVideoCommentValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
    }
}

public sealed class ReplyVideoCommentValidator : AbstractValidator<ReplyVideoCommentCommand>
{
    public ReplyVideoCommentValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CommentId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
    }
}

public sealed class GetVideoCommentsValidator : AbstractValidator<GetVideoCommentsQuery>
{
    public GetVideoCommentsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetVideoRepliesValidator : AbstractValidator<GetVideoRepliesQuery>
{
    public GetVideoRepliesValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CommentId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class DeleteVideoCommentValidator : AbstractValidator<DeleteVideoCommentCommand>
{
    public DeleteVideoCommentValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CommentId).NotEmpty();
    }
}
