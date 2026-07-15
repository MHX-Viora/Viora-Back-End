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
        Assert.NotNull(typeof(FeedController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/feed", typeof(FeedController).GetCustomAttribute<RouteAttribute>()!.Template);

        var action = typeof(FeedController).GetMethod(nameof(FeedController.ListCommunityPosts))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Posts_controller_keeps_common_post_interaction_route()
    {
        Assert.NotNull(typeof(PostsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/posts", typeof(PostsController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public void Posts_controller_exposes_post_interaction_routes()
    {
        var methods = typeof(PostsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(PostsController))
            .ToDictionary(method => method.Name);

        Assert.Equal("{postId:guid}/reactions", methods[nameof(PostsController.React)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}/comments", methods[nameof(PostsController.Comment)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}/comments", methods[nameof(PostsController.ListComments)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("/api/comments/{commentId:guid}/replies", methods[nameof(PostsController.Reply)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("/api/comments/{commentId:guid}/replies", methods[nameof(PostsController.ListReplies)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{postId:guid}/save", methods[nameof(PostsController.Save)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}/share", methods[nameof(PostsController.Share)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}", methods[nameof(PostsController.Delete)].GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        Assert.Equal("{postId:guid}/report", methods[nameof(PostsController.Report)].GetCustomAttribute<HttpPostAttribute>()!.Template);
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
            "Link",
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
            "IsSaved",
            "OriginalPost");
        AssertProperties<PostFeedOriginalPostResponse>(
            "Id",
            "Content",
            "PostType",
            "Visibility",
            "Location",
            "Link",
            "CreatedAt",
            "User",
            "Media",
            "ReactionCount",
            "CommentCount",
            "ShareCount",
            "SaveCount",
            "ViewCount");
        AssertProperties<PostFeedUserResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified");
        AssertProperties<PostFeedMediaResponse>("Id", "MediaUrl", "ThumbnailUrl");
    }

    [Fact]
    public void Post_comment_response_contracts_match_client_shape()
    {
        AssertProperties<PostCommentsResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<CommentRepliesResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<PostCommentListItemResponse>(
            "Id",
            "Content",
            "CreatedAt",
            "UpdatedAt",
            "LikeCount",
            "ReplyCount",
            "IsLiked",
            "User");
        AssertProperties<CommentReplyListItemResponse>(
            "Id",
            "Content",
            "CreatedAt",
            "UpdatedAt",
            "LikeCount",
            "IsLiked",
            "ReplyToUser",
            "User");
        AssertProperties<CommentReplyToUserResponse>("Id", "DisplayName");
    }

    [Fact]
    public void Interaction_response_contracts_match_client_shape()
    {
        AssertProperties<PostReactionResponse>("ReactionType", "ReactionCount");
        AssertProperties<SharePostResponse>("IsShared", "ShareCount");
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
