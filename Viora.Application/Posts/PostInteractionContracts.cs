using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public enum PostInteractionError
{
    NotFound,
    Forbidden,
    Invalid,
    Conflict
}

public sealed record Result<T>(bool IsSuccess, T? Value, PostInteractionError? Error, string? Message)
{
    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(PostInteractionError error, string message) => new(false, default, error, message);
}

public sealed record EmptyResponse;

public sealed record ReactPostCommand(Guid UserId, Guid PostId, ReactionType ReactionType)
    : IRequest<Result<PostReactionResponse>>;

public sealed record CreateCommentCommand(Guid UserId, Guid PostId, string Content)
    : IRequest<Result<CommentResponse>>;

public sealed record ReplyCommentCommand(Guid UserId, Guid CommentId, string Content)
    : IRequest<Result<CommentReplyListItemResponse>>;

public sealed record ToggleSavePostCommand(Guid UserId, Guid PostId)
    : IRequest<Result<SavePostResponse>>;

public sealed record SharePostCommand(Guid UserId, Guid PostId)
    : IRequest<Result<SharePostResponse>>;

public sealed record DeletePostCommand(Guid UserId, Guid PostId)
    : IRequest<Result<EmptyResponse>>;

public sealed record ReportPostCommand(Guid UserId, Guid PostId, ReportReason Reason, string? Description)
    : IRequest<Result<ReportPostResponse>>;

public sealed record GetPostCommentsQuery(Guid UserId, Guid PostId, int Page, int PageSize, string? Sort)
    : IRequest<Result<PostCommentsResponse>>;

public sealed record GetCommentRepliesQuery(Guid UserId, Guid CommentId, int Page, int PageSize, string? Sort)
    : IRequest<Result<CommentRepliesResponse>>;

public sealed record PostReactionResponse(ReactionType? ReactionType, int ReactionCount);
public sealed record SavePostResponse(bool IsSaved, int SaveCount);
public sealed record DeletePostResponse(string Message);
public sealed record ReportPostResponse(Guid Id, string Message);

public sealed record CommentResponse(
    Guid Id,
    PostInteractionUserResponse User,
    string Content,
    DateTime CreatedAt,
    int ReplyCount,
    int LikeCount);

public sealed record SharePostResponse(bool IsShared, int ShareCount);

public sealed record PostInteractionUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record PostCommentsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<PostCommentListItemResponse> Items);

public sealed record CommentRepliesResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<CommentReplyListItemResponse> Items);

public sealed record PostCommentListItemResponse(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int LikeCount,
    int ReplyCount,
    bool IsLiked,
    PostInteractionUserResponse User);

public sealed record CommentReplyListItemResponse(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int LikeCount,
    bool IsLiked,
    CommentReplyToUserResponse? ReplyToUser,
    PostInteractionUserResponse User);

public sealed record CommentReplyToUserResponse(
    Guid Id,
    string DisplayName);

public sealed class ReactPostValidator : AbstractValidator<ReactPostCommand>
{
    public ReactPostValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PostId).NotEmpty();
        RuleFor(x => x.ReactionType).Must(Enum.IsDefined);
    }
}

public sealed class CreateCommentValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PostId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
    }
}

public sealed class ReplyCommentValidator : AbstractValidator<ReplyCommentCommand>
{
    public ReplyCommentValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CommentId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
    }
}

public sealed class ReportPostValidator : AbstractValidator<ReportPostCommand>
{
    public ReportPostValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PostId).NotEmpty();
        RuleFor(x => x.Reason).Must(Enum.IsDefined);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class GetPostCommentsValidator : AbstractValidator<GetPostCommentsQuery>
{
    public GetPostCommentsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PostId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThan(0);
    }
}

public sealed class GetCommentRepliesValidator : AbstractValidator<GetCommentRepliesQuery>
{
    public GetCommentRepliesValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CommentId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThan(0);
    }
}

public interface IPostInteractionRepository
{
    Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Post?> GetPostForInteractionAsync(Guid postId, CancellationToken cancellationToken);
    Task<Post?> GetPostWithOriginalAsync(Guid postId, CancellationToken cancellationToken);
    Task<Comment?> GetCommentForReplyAsync(Guid commentId, CancellationToken cancellationToken);
    Task<Comment?> GetCommentForDeleteAsync(Guid commentId, CancellationToken cancellationToken);
    Task<PostReaction?> GetReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken);
    Task<SavedPost?> GetSavedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken);
    Task<bool> HasReportedPostAsync(Guid postId, Guid userId, CancellationToken cancellationToken);
    Task AddReactionAsync(PostReaction reaction, CancellationToken cancellationToken);
    void RemoveReaction(PostReaction reaction);
    Task AddCommentAsync(Comment comment, CancellationToken cancellationToken);
    Task AddSavedPostAsync(SavedPost savedPost, CancellationToken cancellationToken);
    void RemoveSavedPost(SavedPost savedPost);
    Task AddReportAsync(Report report, CancellationToken cancellationToken);
    Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    Task<bool> CanViewPostAsync(Post post, Guid userId, CancellationToken cancellationToken);
    Task<PostCommentsResponse> GetPostCommentsAsync(GetPostCommentsQuery query, CancellationToken cancellationToken);
    Task<CommentRepliesResponse> GetCommentRepliesAsync(GetCommentRepliesQuery query, CancellationToken cancellationToken);
    Task<VideoCommentsResponse> GetVideoCommentsAsync(GetVideoCommentsQuery query, CancellationToken cancellationToken);
    Task<VideoRepliesResponse> GetVideoRepliesAsync(GetVideoRepliesQuery query, CancellationToken cancellationToken);
}
