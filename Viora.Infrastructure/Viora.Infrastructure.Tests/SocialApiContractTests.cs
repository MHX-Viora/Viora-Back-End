using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Social;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class SocialApiContractTests
{
    [Fact]
    public void User_relationships_controller_exposes_authenticated_routes()
    {
        Assert.NotNull(typeof(UserRelationshipsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/users", typeof(UserRelationshipsController).GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = Methods<UserRelationshipsController>();

        Assert.Equal("me/statistics", methods[nameof(UserRelationshipsController.GetMyStatistics)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{userId:guid}/follow", methods[nameof(UserRelationshipsController.ToggleFollow)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{userId:guid}/profile", methods[nameof(UserRelationshipsController.GetUserProfile)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{userId:guid}/relationship", methods[nameof(UserRelationshipsController.GetRelationship)].GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void Friends_controller_exposes_authenticated_routes()
    {
        Assert.NotNull(typeof(FriendsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/friends", typeof(FriendsController).GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = Methods<FriendsController>();

        Assert.NotNull(methods[nameof(FriendsController.List)].GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal("request", methods[nameof(FriendsController.SendRequest)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("requests", methods[nameof(FriendsController.ListRequests)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{friendshipId:guid}/accept", methods[nameof(FriendsController.Accept)].GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{friendshipId:guid}/reject", methods[nameof(FriendsController.Reject)].GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{id:guid}", methods[nameof(FriendsController.Delete)].GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    [Fact]
    public void Social_response_contracts_match_client_shape()
    {
        AssertProperties<FollowResponse>("IsFollowing", "FollowerCount");
        AssertProperties<FriendshipResponse>("Id", "Status");
        AssertProperties<SendFriendRequestResponse>("Success", "Message", "Data");
        AssertProperties<SendFriendRequestData>("FriendshipId", "Status");
        AssertProperties<FriendshipListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<FriendshipListItemResponse>("FriendshipId", "Status", "CreatedAt", "RespondedAt", "User");
        AssertProperties<FriendshipUserResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified", "MutualFriendCount");
        AssertProperties<FriendshipActionResponse>("Success", "Message");
        AssertProperties<FriendRequestListResponse>("Items");
        AssertProperties<FriendRequestItemResponse>("Id", "UserId", "DisplayName", "AvatarUrl", "IsVerified", "CreatedAt");
        AssertProperties<AcceptFriendResponse>("ConversationId");
        AssertProperties<DeleteFriendResponse>("Status");
        AssertProperties<RelationshipResponse>("IsFollowing", "FriendStatus", "IsRequester", "CanMessage", "ConversationId");
        AssertProperties<UserStatisticsResponse>("PostCount", "FollowerCount", "FollowingCount", "FriendCount");
        AssertProperties<UserProfileSummaryResponse>(
            "Id",
            "DisplayName",
            "AvatarUrl",
            "CoverUrl",
            "Gender",
            "IsVerified",
            "PostCount",
            "FollowerCount",
            "FollowingCount",
            "FriendCount",
            "IsFollowing",
            "Friendship",
            "CanMessage",
            "ConversationId");
        AssertProperties<UserProfileFriendshipResponse>("Status", "IsRequester");
    }

    [Fact]
    public async Task Follow_validator_rejects_self_follow()
    {
        var userId = Guid.NewGuid();
        var validator = new ToggleFollowValidator();

        var result = await validator.ValidateAsync(new ToggleFollowCommand(userId, userId));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Friend_request_validator_rejects_self_request()
    {
        var userId = Guid.NewGuid();
        var validator = new SendFriendRequestValidator();

        var result = await validator.ValidateAsync(new SendFriendRequestCommand(userId, userId));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Notification_type_includes_friend_rejected()
    {
        Assert.True(Enum.IsDefined(NotificationType.FriendRejected));
    }

    [Fact]
    public void Friendship_status_includes_unfriended()
    {
        Assert.True(Enum.IsDefined(FriendshipStatus.Unfriended));
        Assert.Equal((short)5, (short)FriendshipStatus.Unfriended);
    }

    [Fact]
    public async Task Delete_friend_marks_accepted_friendship_as_unfriended()
    {
        var currentUserId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterUserId = currentUserId,
            AddresseeUserId = friendId,
            Status = FriendshipStatus.Accepted
        };
        var repository = new FakeSocialRepository(currentUserId, friendId, friendship);
        var handler = new DeleteFriendHandler(repository, new DeleteFriendValidator());

        var result = await handler.Handle(new DeleteFriendCommand(currentUserId, friendId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FriendshipStatus.Unfriended, friendship.Status);
        Assert.Equal(FriendshipStatus.Unfriended, result.Value!.Status);
        Assert.NotNull(friendship.RespondedAt);
        Assert.True(repository.SavedChanges);
    }

    [Fact]
    public async Task Delete_friend_accepts_accepted_friendship_id()
    {
        var currentUserId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterUserId = friendId,
            AddresseeUserId = currentUserId,
            Status = FriendshipStatus.Accepted
        };
        var repository = new FakeSocialRepository(currentUserId, friendId, friendship);
        var handler = new DeleteFriendHandler(repository, new DeleteFriendValidator());

        var result = await handler.Handle(new DeleteFriendCommand(currentUserId, friendship.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FriendshipStatus.Unfriended, friendship.Status);
        Assert.Equal(FriendshipStatus.Unfriended, result.Value!.Status);
        Assert.True(repository.SavedChanges);
    }

    [Fact]
    public async Task Delete_friend_cancels_pending_request_by_target_user_id()
    {
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterUserId = currentUserId,
            AddresseeUserId = targetUserId,
            Status = FriendshipStatus.Pending
        };
        var repository = new FakeSocialRepository(currentUserId, targetUserId, friendship);
        var handler = new DeleteFriendHandler(repository, new DeleteFriendValidator());

        var result = await handler.Handle(new DeleteFriendCommand(currentUserId, targetUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FriendshipStatus.Cancelled, friendship.Status);
        Assert.Equal(FriendshipStatus.Cancelled, result.Value!.Status);
        Assert.True(repository.SavedChanges);
    }

    [Fact]
    public async Task Send_friend_request_reuses_unfriended_friendship_as_pending()
    {
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterUserId = targetUserId,
            AddresseeUserId = currentUserId,
            Status = FriendshipStatus.Unfriended,
            RespondedAt = DateTime.UtcNow
        };
        var repository = new FakeSocialRepository(currentUserId, targetUserId, friendship);
        var handler = new SendFriendRequestHandler(repository, new SendFriendRequestValidator());

        var result = await handler.Handle(new SendFriendRequestCommand(currentUserId, targetUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
        Assert.Equal(currentUserId, friendship.RequesterUserId);
        Assert.Equal(targetUserId, friendship.AddresseeUserId);
        Assert.Null(friendship.RespondedAt);
        Assert.Equal("Pending", result.Value!.Data.Status);
        Assert.Single(repository.Notifications);
    }

    [Fact]
    public async Task Send_friend_request_creates_pending_friendship_when_none_exists()
    {
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var repository = new FakeSocialRepository(currentUserId, targetUserId, null);
        var handler = new SendFriendRequestHandler(repository, new SendFriendRequestValidator());

        var result = await handler.Handle(new SendFriendRequestCommand(currentUserId, targetUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Friendship);
        Assert.Equal(FriendshipStatus.Pending, repository.Friendship.Status);
        Assert.Equal(currentUserId, repository.Friendship.RequesterUserId);
        Assert.Equal(targetUserId, repository.Friendship.AddresseeUserId);
        Assert.Equal(repository.Friendship.Id, result.Value!.Data.FriendshipId);
        Assert.Single(repository.Notifications);
    }

    [Fact]
    public async Task User_profile_validator_rejects_missing_target_user()
    {
        var validator = new GetUserProfileValidator();

        var result = await validator.ValidateAsync(new GetUserProfileQuery(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
    }

    private static Dictionary<string, MethodInfo> Methods<T>() =>
        typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(T))
            .ToDictionary(method => method.Name);

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }

    private sealed class FakeSocialRepository(Guid currentUserId, Guid targetUserId, Friendship? friendship) : ISocialRepository
    {
        public bool SavedChanges { get; private set; }
        public List<Notification> Notifications { get; } = [];
        public Friendship? Friendship => friendship;

        public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(userId == currentUserId || userId == targetUserId
                ? new User
                {
                    Id = userId,
                    DisplayName = userId == currentUserId ? "Current" : "Target",
                    Account = new Account { Status = AccountStatus.Active }
                }
                : null);

        public Task<Follow?> GetFollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken) =>
            Task.FromResult<Follow?>(null);

        public Task<int> CountFollowersAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Friendship?> GetFriendshipBetweenAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken) =>
            Task.FromResult(friendship);

        public Task<Friendship?> GetFriendshipAsync(Guid friendshipId, CancellationToken cancellationToken) =>
            Task.FromResult(friendship?.Id == friendshipId ? friendship : null);

        public Task<Guid?> GetPrivateConversationIdAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<FriendRequestListResponse> GetPendingRequestsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(new FriendRequestListResponse([]));

        public Task<FriendshipListResponse> GetFriendshipsAsync(GetFriendshipsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new FriendshipListResponse(query.Page, query.PageSize, 0, 0, []));

        public Task<UserStatisticsResponse?> GetStatisticsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserStatisticsResponse?>(null);

        public Task<UserProfileSummaryResponse?> GetUserProfileSummaryAsync(Guid currentUserId, Guid targetUserId, CancellationToken cancellationToken) =>
            Task.FromResult<UserProfileSummaryResponse?>(null);

        public Task AddFollowAsync(Follow follow, CancellationToken cancellationToken) => Task.CompletedTask;

        public void RemoveFollow(Follow follow)
        {
        }

        public Task AddFriendshipAsync(Friendship newFriendship, CancellationToken cancellationToken)
        {
            friendship = newFriendship;
            return Task.CompletedTask;
        }

        public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SavedChanges = true;
            return Task.CompletedTask;
        }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            await operation(cancellationToken);
            SavedChanges = true;
        }
    }
}
