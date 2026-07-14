using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class PostFeedApiContractTests
{
    [Fact]
    public void Posts_controller_exposes_authenticated_paginated_get()
    {
        Assert.NotNull(typeof(PostsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/posts", typeof(PostsController).GetCustomAttribute<RouteAttribute>()!.Template);

        var action = typeof(PostsController).GetMethod(nameof(PostsController.ListCommunityPosts))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Post_feed_response_contract_has_only_expected_fields()
    {
        AssertProperties<PostFeedResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<PostFeedItemResponse>(
            "Id",
            "Content",
            "PostType",
            "Visibility",
            "Location",
            "CreatedAt",
            "User",
            "Media",
            "ReactionCount",
            "CommentCount",
            "ShareCount",
            "SaveCount",
            "ViewCount",
            "IsReacted",
            "ReactionType",
            "IsSaved");
        AssertProperties<PostFeedUserResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified");
        AssertProperties<PostFeedMediaResponse>("Id", "MediaUrl", "ThumbnailUrl");
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
