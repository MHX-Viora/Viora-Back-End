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

        Assert.Equal("request", methods[nameof(FriendsController.SendRequest)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("requests", methods[nameof(FriendsController.ListRequests)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{friendshipId:guid}/accept", methods[nameof(FriendsController.Accept)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{friendshipId:guid}/reject", methods[nameof(FriendsController.Reject)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:guid}", methods[nameof(FriendsController.Delete)].GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    [Fact]
    public void Social_response_contracts_match_client_shape()
    {
        AssertProperties<FollowResponse>("IsFollowing", "FollowerCount");
        AssertProperties<FriendshipResponse>("Id", "Status");
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
}
