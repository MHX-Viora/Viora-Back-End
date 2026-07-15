using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class VideoFeedApiContractTests
{
    [Fact]
    public void Reels_controller_exposes_authenticated_paginated_get()
    {
        Assert.NotNull(typeof(ReelsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/reels", typeof(ReelsController).GetCustomAttribute<RouteAttribute>()!.Template);

        var action = typeof(ReelsController).GetMethod(nameof(ReelsController.List))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());

        var createAction = typeof(ReelsController).GetMethod(nameof(ReelsController.Create))!;
        Assert.NotNull(createAction.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("multipart/form-data", createAction.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes.Single());
    }

    [Fact]
    public void Reels_controller_exposes_only_reel_specific_routes()
    {
        var methods = typeof(ReelsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(ReelsController))
            .ToDictionary(method => method.Name);

        Assert.DoesNotContain(methods.Keys, name => name is "ToggleReaction" or "ToggleSave" or "Share" or "CreateComment" or "ReplyComment" or "ListComments" or "ListReplies" or "DeleteComment");
    }

    [Fact]
    public void Video_feed_response_contract_has_only_expected_fields()
    {
        AssertProperties<VideoFeedResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<VideoFeedItemResponse>(
            "Id",
            "Content",
            "Location",
            "CreatedAt",
            "ViewCount",
            "ReactionCount",
            "CommentCount",
            "ShareCount",
            "SaveCount",
            "IsSaved",
            "IsReacted",
            "ReactionType",
            "Media",
            "Hashtags",
            "User");
        AssertProperties<VideoFeedMediaResponse>("Id", "MediaUrl", "ThumbnailUrl");
        AssertProperties<VideoFeedUserResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified", "IsFollowing");
    }

    [Fact]
    public void Video_interaction_response_contracts_have_expected_fields()
    {
        AssertProperties<CreateReelResponse>(
            "Id",
            "Content",
            "PostType",
            "Visibility",
            "ReactionCount",
            "CommentCount",
            "ShareCount",
            "SaveCount",
            "ViewCount",
            "CreatedAt",
            "User",
            "Media",
            "Hashtags",
            "IsReacted",
            "IsSaved");
        AssertProperties<ReelHashtagResponse>("Id", "Name");
        AssertProperties<VideoReactionResponse>("IsLiked", "ReactionCount");
        AssertProperties<VideoShareResponse>("ShareCount");
        AssertProperties<SavePostResponse>("IsSaved", "SaveCount");
        AssertProperties<VideoCommentResponse>("Id", "Content", "CreatedAt", "LikeCount", "ReplyCount", "User");
        AssertProperties<VideoCommentListItemResponse>("Id", "Content", "CreatedAt", "LikeCount", "ReplyCount", "IsLiked", "User");
        AssertProperties<VideoReplyResponse>("Id", "Content", "CreatedAt", "LikeCount", "ReplyCount", "User");
        AssertProperties<VideoReplyListItemResponse>("Id", "Content", "CreatedAt", "LikeCount", "IsLiked", "ReplyToUser", "User");
        AssertProperties<VideoCommentsResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<VideoRepliesResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
    }

    [Fact]
    public void Video_reaction_uses_love_reaction_type_for_heart()
    {
        Assert.Equal(1, (short)ReactionType.Love);
    }

    [Fact]
    public async Task Video_feed_validator_requires_user_id_for_user_sort()
    {
        var validator = new GetShortVideosValidator();

        var result = await validator.ValidateAsync(new GetShortVideosQuery(
            1,
            10,
            "user",
            null,
            null,
            Guid.NewGuid()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Video_feed_validator_accepts_supported_sort()
    {
        var validator = new GetShortVideosValidator();

        var result = await validator.ValidateAsync(new GetShortVideosQuery(
            1,
            10,
            "recommend",
            "travel",
            null,
            Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
