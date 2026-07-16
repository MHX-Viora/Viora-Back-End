using FluentValidation;
using MediatR;
using Viora.Application.Notifications;
using Viora.Domain.Entities;

namespace Viora.Application.Social;

public sealed class ToggleFollowHandler(
    ISocialRepository repository,
    IValidator<ToggleFollowCommand> validator)
    : IRequestHandler<ToggleFollowCommand, SocialResult<FollowResponse>>
{
    public async Task<SocialResult<FollowResponse>> Handle(ToggleFollowCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<FollowResponse>.Failure(SocialError.Invalid, FirstError(validation));
        var currentUser = await repository.GetActiveUserAsync(request.CurrentUserId, cancellationToken);
        var targetUser = await repository.GetActiveUserAsync(request.TargetUserId, cancellationToken);
        if (currentUser is null || targetUser is null)
        {
            return SocialResult<FollowResponse>.Failure(SocialError.NotFound, "Khong tim thay nguoi dung.");
        }

        var isFollowing = false;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            var follow = await repository.GetFollowAsync(request.CurrentUserId, request.TargetUserId, token);
            if (follow is null)
            {
                await repository.AddFollowAsync(new Follow
                {
                    FollowerId = request.CurrentUserId,
                    FollowingId = request.TargetUserId
                }, token);
                await AddNotificationAsync(repository, request.TargetUserId, currentUser, NotificationType.Follow, request.CurrentUserId, NotificationReferenceType.User, token);
                isFollowing = true;
                return;
            }

            repository.RemoveFollow(follow);
        }, cancellationToken);

        var count = await repository.CountFollowersAsync(request.TargetUserId, cancellationToken);
        return SocialResult<FollowResponse>.Success(new FollowResponse(isFollowing, count));
    }

    internal static string FirstError(FluentValidation.Results.ValidationResult validation) =>
        validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Du lieu khong hop le.";

    internal static Task AddNotificationAsync(
        ISocialRepository repository,
        Guid userId,
        User sender,
        NotificationType type,
        Guid referenceId,
        NotificationReferenceType referenceType,
        CancellationToken cancellationToken) =>
        repository.AddNotificationAsync(
            NotificationFactory.Create(userId, type, sender, referenceId, referenceType),
            cancellationToken);
}

public sealed class SendFriendRequestHandler(
    ISocialRepository repository,
    IValidator<SendFriendRequestCommand> validator)
    : IRequestHandler<SendFriendRequestCommand, SocialResult<SendFriendRequestResponse>>
{
    public async Task<SocialResult<SendFriendRequestResponse>> Handle(SendFriendRequestCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<SendFriendRequestResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));
        var currentUser = await repository.GetActiveUserAsync(request.CurrentUserId, cancellationToken);
        var targetUser = await repository.GetActiveUserAsync(request.TargetUserId, cancellationToken);
        if (currentUser is null || targetUser is null)
        {
            return SocialResult<SendFriendRequestResponse>.Failure(SocialError.NotFound, "Khong tim thay nguoi dung.");
        }

        var friendship = await repository.GetFriendshipBetweenAsync(request.CurrentUserId, request.TargetUserId, cancellationToken);
        if (friendship is not null)
        {
            var error = GetExistingFriendshipError(friendship, request.CurrentUserId);
            if (error is not null)
            {
                return SocialResult<SendFriendRequestResponse>.Failure(SocialError.Conflict, error);
            }
        }
        else
        {
            friendship = new Friendship();
        }

        await repository.ExecuteInTransactionAsync(async token =>
        {
            if (friendship.Id == Guid.Empty)
            {
                friendship.Id = Guid.NewGuid();
            }

            var isNew = friendship.CreatedAt == default;
            friendship.RequesterUserId = request.CurrentUserId;
            friendship.AddresseeUserId = request.TargetUserId;
            friendship.Status = FriendshipStatus.Pending;
            friendship.RespondedAt = null;

            if (isNew)
            {
                await repository.AddFriendshipAsync(friendship, token);
            }

            await ToggleFollowHandler.AddNotificationAsync(repository, request.TargetUserId, currentUser, NotificationType.FriendRequest, friendship.Id, NotificationReferenceType.User, token);
        }, cancellationToken);

        return SocialResult<SendFriendRequestResponse>.Success(new SendFriendRequestResponse(
            true,
            "Đã gửi lời mời kết bạn.",
            new SendFriendRequestData(friendship.Id, FriendshipStatus.Pending.ToString())));
    }

    private static string? GetExistingFriendshipError(Friendship friendship, Guid currentUserId) =>
        friendship.Status switch
        {
            FriendshipStatus.Pending when friendship.RequesterUserId == currentUserId => "Bạn đã gửi lời mời kết bạn.",
            FriendshipStatus.Pending => "Người này đã gửi lời mời cho bạn.",
            FriendshipStatus.Accepted => "Hai người đã là bạn.",
            FriendshipStatus.Blocked => "Không thể gửi lời mời.",
            FriendshipStatus.Cancelled or FriendshipStatus.Rejected or FriendshipStatus.Unfriended => null,
            _ => "Không thể gửi lời mời."
        };
}

public sealed class GetFriendRequestsHandler(
    ISocialRepository repository,
    IValidator<GetFriendRequestsQuery> validator)
    : IRequestHandler<GetFriendRequestsQuery, SocialResult<FriendRequestListResponse>>
{
    public async Task<SocialResult<FriendRequestListResponse>> Handle(GetFriendRequestsQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<FriendRequestListResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));

        return SocialResult<FriendRequestListResponse>.Success(
            await repository.GetPendingRequestsAsync(request.CurrentUserId, cancellationToken));
    }
}

public sealed class GetFriendshipsHandler(
    ISocialRepository repository,
    IValidator<GetFriendshipsQuery> validator)
    : IRequestHandler<GetFriendshipsQuery, SocialResult<FriendshipListResponse>>
{
    public async Task<SocialResult<FriendshipListResponse>> Handle(GetFriendshipsQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<FriendshipListResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));

        var response = await repository.GetFriendshipsAsync(
            request with
            {
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 100),
                Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim()
            },
            cancellationToken);

        return SocialResult<FriendshipListResponse>.Success(response);
    }
}

public sealed class AcceptFriendRequestHandler(
    ISocialRepository repository,
    IValidator<AcceptFriendRequestCommand> validator)
    : IRequestHandler<AcceptFriendRequestCommand, SocialResult<FriendshipActionResponse>>
{
    public async Task<SocialResult<FriendshipActionResponse>> Handle(AcceptFriendRequestCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<FriendshipActionResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));
        var friendship = await repository.GetFriendshipAsync(request.FriendshipId, cancellationToken);
        if (friendship is null) return SocialResult<FriendshipActionResponse>.Failure(SocialError.NotFound, "Khong tim thay loi moi ket ban.");
        if (friendship.AddresseeUserId != request.CurrentUserId) return SocialResult<FriendshipActionResponse>.Failure(SocialError.Forbidden, "Ban khong co quyen dong y loi moi nay.");
        if (friendship.Status != FriendshipStatus.Pending) return SocialResult<FriendshipActionResponse>.Failure(SocialError.Invalid, "Loi moi ket ban khong con cho xu ly.");

        var currentUser = await repository.GetActiveUserAsync(request.CurrentUserId, cancellationToken);
        if (currentUser is null) return SocialResult<FriendshipActionResponse>.Failure(SocialError.NotFound, "Khong tim thay nguoi dung.");

        await repository.ExecuteInTransactionAsync(async token =>
        {
            friendship.Status = FriendshipStatus.Accepted;
            friendship.RespondedAt = DateTime.UtcNow;
            var existingConversationId = await repository.GetPrivateConversationIdAsync(
                friendship.RequesterUserId,
                friendship.AddresseeUserId,
                token);
            if (!existingConversationId.HasValue)
            {
                var now = DateTime.UtcNow;
                var conversation = new Conversation
                {
                    ConversationType = ConversationType.Private,
                    CanSendMessage = ConversationSendPermission.Everyone,
                    CreatedBy = request.CurrentUserId
                };
                conversation.Members.Add(new ConversationMember
                {
                    Conversation = conversation,
                    UserId = friendship.RequesterUserId,
                    Role = ConversationMemberRole.Member,
                    Status = ConversationMemberStatus.Active,
                    JoinedAt = now,
                    JoinedBy = request.CurrentUserId
                });
                conversation.Members.Add(new ConversationMember
                {
                    Conversation = conversation,
                    UserId = friendship.AddresseeUserId,
                    Role = ConversationMemberRole.Member,
                    Status = ConversationMemberStatus.Active,
                    JoinedAt = now,
                    JoinedBy = request.CurrentUserId
                });
                await repository.AddConversationAsync(conversation, token);
            }
            await ToggleFollowHandler.AddNotificationAsync(repository, friendship.RequesterUserId, currentUser, NotificationType.FriendAccepted, friendship.Id, NotificationReferenceType.User, token);
        }, cancellationToken);

        return SocialResult<FriendshipActionResponse>.Success(new FriendshipActionResponse(true, "Đã đồng ý lời mời kết bạn."));
    }
}

public sealed class RejectFriendRequestHandler(
    ISocialRepository repository,
    IValidator<RejectFriendRequestCommand> validator)
    : IRequestHandler<RejectFriendRequestCommand, SocialResult<FriendshipActionResponse>>
{
    public async Task<SocialResult<FriendshipActionResponse>> Handle(RejectFriendRequestCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<FriendshipActionResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));
        var friendship = await repository.GetFriendshipAsync(request.FriendshipId, cancellationToken);
        if (friendship is null) return SocialResult<FriendshipActionResponse>.Failure(SocialError.NotFound, "Khong tim thay loi moi ket ban.");
        if (friendship.AddresseeUserId != request.CurrentUserId) return SocialResult<FriendshipActionResponse>.Failure(SocialError.Forbidden, "Ban khong co quyen tu choi loi moi nay.");
        if (friendship.Status != FriendshipStatus.Pending) return SocialResult<FriendshipActionResponse>.Failure(SocialError.Invalid, "Loi moi ket ban khong con cho xu ly.");

        await repository.ExecuteInTransactionAsync(async token =>
        {
            friendship.Status = FriendshipStatus.Rejected;
            friendship.RespondedAt = DateTime.UtcNow;
            await Task.CompletedTask;
        }, cancellationToken);

        return SocialResult<FriendshipActionResponse>.Success(new FriendshipActionResponse(true, "Đã từ chối lời mời kết bạn."));
    }
}

public sealed class DeleteFriendHandler(
    ISocialRepository repository,
    IValidator<DeleteFriendCommand> validator)
    : IRequestHandler<DeleteFriendCommand, SocialResult<DeleteFriendResponse>>
{
    public async Task<SocialResult<DeleteFriendResponse>> Handle(DeleteFriendCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<DeleteFriendResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));

        var friendship = await repository.GetFriendshipAsync(request.Id, cancellationToken);
        if (friendship is not null)
        {
            if (friendship.Status == FriendshipStatus.Pending)
            {
                if (friendship.RequesterUserId != request.CurrentUserId) return SocialResult<DeleteFriendResponse>.Failure(SocialError.Forbidden, "Chi nguoi gui moi duoc huy loi moi.");
                friendship.Status = FriendshipStatus.Cancelled;
            }
            else if (friendship.Status == FriendshipStatus.Accepted)
            {
                if (friendship.RequesterUserId != request.CurrentUserId && friendship.AddresseeUserId != request.CurrentUserId) return SocialResult<DeleteFriendResponse>.Failure(SocialError.Forbidden, "Ban khong co quyen huy ket ban.");
                friendship.Status = FriendshipStatus.Unfriended;
            }
            else
            {
                return SocialResult<DeleteFriendResponse>.Failure(SocialError.Invalid, "Quan he ban be khong con hieu luc.");
            }

            friendship.RespondedAt = DateTime.UtcNow;
            await repository.SaveChangesAsync(cancellationToken);
            return SocialResult<DeleteFriendResponse>.Success(new DeleteFriendResponse(friendship.Status));
        }

        friendship = await repository.GetFriendshipBetweenAsync(request.CurrentUserId, request.Id, cancellationToken);
        if (friendship is null)
        {
            return SocialResult<DeleteFriendResponse>.Failure(SocialError.NotFound, "Khong tim thay quan he ban be.");
        }

        if (friendship.Status == FriendshipStatus.Pending)
        {
            if (friendship.RequesterUserId != request.CurrentUserId) return SocialResult<DeleteFriendResponse>.Failure(SocialError.Forbidden, "Chi nguoi gui moi duoc huy loi moi.");
            friendship.Status = FriendshipStatus.Cancelled;
        }
        else if (friendship.Status == FriendshipStatus.Accepted)
        {
            friendship.Status = FriendshipStatus.Unfriended;
        }
        else
        {
            return SocialResult<DeleteFriendResponse>.Failure(SocialError.Invalid, "Quan he ban be khong con hieu luc.");
        }

        friendship.RespondedAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return SocialResult<DeleteFriendResponse>.Success(new DeleteFriendResponse(friendship.Status));
    }
}

public sealed class GetRelationshipHandler(
    ISocialRepository repository,
    IValidator<GetRelationshipQuery> validator)
    : IRequestHandler<GetRelationshipQuery, SocialResult<RelationshipResponse>>
{
    public async Task<SocialResult<RelationshipResponse>> Handle(GetRelationshipQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<RelationshipResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));
        if (await repository.GetActiveUserAsync(request.TargetUserId, cancellationToken) is null)
        {
            return SocialResult<RelationshipResponse>.Failure(SocialError.NotFound, "Khong tim thay nguoi dung.");
        }

        var follow = await repository.GetFollowAsync(request.CurrentUserId, request.TargetUserId, cancellationToken);
        var friendship = await repository.GetFriendshipBetweenAsync(request.CurrentUserId, request.TargetUserId, cancellationToken);
        var conversationId = await repository.GetPrivateConversationIdAsync(request.CurrentUserId, request.TargetUserId, cancellationToken);
        var canMessage = friendship?.Status == FriendshipStatus.Accepted && conversationId.HasValue;

        return SocialResult<RelationshipResponse>.Success(new RelationshipResponse(
            follow is not null,
            friendship?.Status,
            friendship is not null && friendship.RequesterUserId == request.CurrentUserId,
            canMessage,
            conversationId));
    }
}

public sealed class GetMyStatisticsHandler(
    ISocialRepository repository,
    IValidator<GetMyStatisticsQuery> validator)
    : IRequestHandler<GetMyStatisticsQuery, SocialResult<UserStatisticsResponse>>
{
    public async Task<SocialResult<UserStatisticsResponse>> Handle(GetMyStatisticsQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<UserStatisticsResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));

        var response = await repository.GetStatisticsAsync(request.CurrentUserId, cancellationToken);
        return response is null
            ? SocialResult<UserStatisticsResponse>.Failure(SocialError.NotFound, "Khong tim thay nguoi dung.")
            : SocialResult<UserStatisticsResponse>.Success(response);
    }
}

public sealed class GetUserProfileHandler(
    ISocialRepository repository,
    IValidator<GetUserProfileQuery> validator)
    : IRequestHandler<GetUserProfileQuery, SocialResult<UserProfileSummaryResponse>>
{
    public async Task<SocialResult<UserProfileSummaryResponse>> Handle(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return SocialResult<UserProfileSummaryResponse>.Failure(SocialError.Invalid, ToggleFollowHandler.FirstError(validation));

        var response = await repository.GetUserProfileSummaryAsync(request.CurrentUserId, request.TargetUserId, cancellationToken);
        return response is null
            ? SocialResult<UserProfileSummaryResponse>.Failure(SocialError.NotFound, "Khong tim thay nguoi dung.")
            : SocialResult<UserProfileSummaryResponse>.Success(response);
    }
}
