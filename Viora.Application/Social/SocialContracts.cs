using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Social;

public enum SocialError
{
    NotFound,
    Forbidden,
    Invalid,
    Conflict
}

public sealed record SocialResult<T>(bool IsSuccess, T? Value, SocialError? Error, string? Message)
{
    public static SocialResult<T> Success(T value) => new(true, value, null, null);
    public static SocialResult<T> Failure(SocialError error, string message) => new(false, default, error, message);
}

public sealed record ToggleFollowCommand(Guid CurrentUserId, Guid TargetUserId)
    : IRequest<SocialResult<FollowResponse>>;

public sealed record SendFriendRequestCommand(Guid CurrentUserId, Guid TargetUserId)
    : IRequest<SocialResult<SendFriendRequestResponse>>;

public sealed record GetFriendRequestsQuery(Guid CurrentUserId)
    : IRequest<SocialResult<FriendRequestListResponse>>;

public sealed record GetFriendshipsQuery(
    Guid CurrentUserId,
    int Page,
    int PageSize,
    FriendshipStatus Status,
    string? Keyword) : IRequest<SocialResult<FriendshipListResponse>>;

public sealed record AcceptFriendRequestCommand(Guid CurrentUserId, Guid FriendshipId)
    : IRequest<SocialResult<FriendshipActionResponse>>;

public sealed record RejectFriendRequestCommand(Guid CurrentUserId, Guid FriendshipId)
    : IRequest<SocialResult<FriendshipActionResponse>>;

public sealed record DeleteFriendCommand(Guid CurrentUserId, Guid Id)
    : IRequest<SocialResult<DeleteFriendResponse>>;

public sealed record GetRelationshipQuery(Guid CurrentUserId, Guid TargetUserId)
    : IRequest<SocialResult<RelationshipResponse>>;

public sealed record GetMyStatisticsQuery(Guid CurrentUserId)
    : IRequest<SocialResult<UserStatisticsResponse>>;

public sealed record GetUserProfileQuery(Guid CurrentUserId, Guid TargetUserId)
    : IRequest<SocialResult<UserProfileSummaryResponse>>;

public sealed record FollowResponse(bool IsFollowing, int FollowerCount);

public sealed record FriendshipResponse(Guid Id, FriendshipStatus Status);

public sealed record SendFriendRequestResponse(
    bool Success,
    string Message,
    SendFriendRequestData Data);

public sealed record SendFriendRequestData(
    Guid FriendshipId,
    string Status);

public sealed record FriendRequestListResponse(IReadOnlyList<FriendRequestItemResponse> Items);

public sealed record FriendshipListResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<FriendshipListItemResponse> Items);

public sealed record FriendshipListItemResponse(
    Guid FriendshipId,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    FriendshipUserResponse User);

public sealed record FriendshipUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified,
    int MutualFriendCount);

public sealed record FriendRequestItemResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified,
    DateTime CreatedAt);

public sealed record AcceptFriendResponse(Guid ConversationId);

public sealed record FriendshipActionResponse(
    bool Success,
    string Message);

public sealed record DeleteFriendResponse(FriendshipStatus? Status);

public sealed record RelationshipResponse(
    bool IsFollowing,
    FriendshipStatus? FriendStatus,
    bool IsRequester,
    bool CanMessage,
    Guid? ConversationId);

public sealed record UserStatisticsResponse(
    int PostCount,
    int FollowerCount,
    int FollowingCount,
    int FriendCount);

public sealed record UserProfileSummaryResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string? CoverUrl,
    Gender Gender,
    bool IsVerified,
    int PostCount,
    int FollowerCount,
    int FollowingCount,
    int FriendCount,
    bool IsFollowing,
    UserProfileFriendshipResponse Friendship,
    bool CanMessage,
    Guid? ConversationId);

public sealed record UserProfileFriendshipResponse(
    string Status,
    bool IsRequester);

public sealed class ToggleFollowValidator : AbstractValidator<ToggleFollowCommand>
{
    public ToggleFollowValidator()
    {
        RuleFor(command => command.CurrentUserId).NotEmpty();
        RuleFor(command => command.TargetUserId).NotEmpty();
        RuleFor(command => command).Must(command => command.CurrentUserId != command.TargetUserId)
            .WithMessage("Khong the theo doi chinh minh.");
    }
}

public sealed class SendFriendRequestValidator : AbstractValidator<SendFriendRequestCommand>
{
    public SendFriendRequestValidator()
    {
        RuleFor(command => command.CurrentUserId).NotEmpty();
        RuleFor(command => command.TargetUserId).NotEmpty();
        RuleFor(command => command).Must(command => command.CurrentUserId != command.TargetUserId)
            .WithMessage("Khong the gui loi moi ket ban cho chinh minh.");
    }
}

public sealed class GetFriendRequestsValidator : AbstractValidator<GetFriendRequestsQuery>
{
    public GetFriendRequestsValidator() => RuleFor(query => query.CurrentUserId).NotEmpty();
}

public sealed class GetFriendshipsValidator : AbstractValidator<GetFriendshipsQuery>
{
    public GetFriendshipsValidator()
    {
        RuleFor(query => query.CurrentUserId).NotEmpty();
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).GreaterThan(0);
        RuleFor(query => query.Status).Must(status =>
            status is FriendshipStatus.Pending or FriendshipStatus.Accepted or FriendshipStatus.Rejected);
        RuleFor(query => query.Keyword).MaximumLength(255);
    }
}

public sealed class AcceptFriendRequestValidator : AbstractValidator<AcceptFriendRequestCommand>
{
    public AcceptFriendRequestValidator()
    {
        RuleFor(command => command.CurrentUserId).NotEmpty();
        RuleFor(command => command.FriendshipId).NotEmpty();
    }
}

public sealed class RejectFriendRequestValidator : AbstractValidator<RejectFriendRequestCommand>
{
    public RejectFriendRequestValidator()
    {
        RuleFor(command => command.CurrentUserId).NotEmpty();
        RuleFor(command => command.FriendshipId).NotEmpty();
    }
}

public sealed class DeleteFriendValidator : AbstractValidator<DeleteFriendCommand>
{
    public DeleteFriendValidator()
    {
        RuleFor(command => command.CurrentUserId).NotEmpty();
        RuleFor(command => command.Id).NotEmpty();
    }
}

public sealed class GetRelationshipValidator : AbstractValidator<GetRelationshipQuery>
{
    public GetRelationshipValidator()
    {
        RuleFor(query => query.CurrentUserId).NotEmpty();
        RuleFor(query => query.TargetUserId).NotEmpty();
    }
}

public sealed class GetMyStatisticsValidator : AbstractValidator<GetMyStatisticsQuery>
{
    public GetMyStatisticsValidator() => RuleFor(query => query.CurrentUserId).NotEmpty();
}

public sealed class GetUserProfileValidator : AbstractValidator<GetUserProfileQuery>
{
    public GetUserProfileValidator()
    {
        RuleFor(query => query.CurrentUserId).NotEmpty();
        RuleFor(query => query.TargetUserId).NotEmpty();
    }
}

public interface ISocialRepository
{
    Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Follow?> GetFollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken);
    Task<int> CountFollowersAsync(Guid userId, CancellationToken cancellationToken);
    Task<Friendship?> GetFriendshipBetweenAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken);
    Task<Friendship?> GetFriendshipAsync(Guid friendshipId, CancellationToken cancellationToken);
    Task<Guid?> GetPrivateConversationIdAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken);
    Task<FriendRequestListResponse> GetPendingRequestsAsync(Guid userId, CancellationToken cancellationToken);
    Task<FriendshipListResponse> GetFriendshipsAsync(GetFriendshipsQuery query, CancellationToken cancellationToken);
    Task<UserStatisticsResponse?> GetStatisticsAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfileSummaryResponse?> GetUserProfileSummaryAsync(Guid currentUserId, Guid targetUserId, CancellationToken cancellationToken);
    Task AddFollowAsync(Follow follow, CancellationToken cancellationToken);
    void RemoveFollow(Follow follow);
    Task AddFriendshipAsync(Friendship friendship, CancellationToken cancellationToken);
    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken);
    Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
