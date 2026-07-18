using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

        var userIdParameter = action.GetParameters().Single(parameter => parameter.Name == "userId");
        Assert.Equal(typeof(Guid?), userIdParameter.ParameterType);
        Assert.NotNull(userIdParameter.GetCustomAttribute<FromQueryAttribute>());
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
        Assert.Equal("{postId:guid}", methods[nameof(PostsController.Detail)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{postId:guid}/comments", methods[nameof(PostsController.Comment)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}/comments", methods[nameof(PostsController.ListComments)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("/api/comments/{commentId:guid}/replies", methods[nameof(PostsController.Reply)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("/api/comments/{commentId:guid}/replies", methods[nameof(PostsController.ListReplies)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{postId:guid}/save", methods[nameof(PostsController.Save)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}/share", methods[nameof(PostsController.Share)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{postId:guid}", methods[nameof(PostsController.Delete)].GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        Assert.Equal("{postId:guid}/report", methods[nameof(PostsController.Report)].GetCustomAttribute<HttpPostAttribute>()!.Template);
        var detailResponses = methods[nameof(PostsController.Detail)].GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        Assert.Contains(detailResponses, attribute => attribute.StatusCode == StatusCodes.Status200OK && attribute.Type == typeof(PostDetailResponse));
        Assert.Contains(detailResponses, attribute => attribute.StatusCode == StatusCodes.Status403Forbidden && attribute.Type == typeof(ProblemDetails));
        Assert.Contains(detailResponses, attribute => attribute.StatusCode == StatusCodes.Status404NotFound && attribute.Type == typeof(ProblemDetails));
        Assert.Equal(
            typeof(DeletePostResponse),
            methods[nameof(PostsController.Delete)]
                .GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK)
                .Type);

        var replyResponse = methods[nameof(PostsController.Reply)]
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status201Created);
        Assert.Equal(typeof(CommentReplyListItemResponse), replyResponse.Type);
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
    public void Post_detail_response_contract_has_only_expected_fields()
    {
        AssertProperties<PostDetailResponse>(
            "Id", "PostType", "Content", "Visibility", "Location", "CreatedAt", "UpdatedAt",
            "ReactionCount", "CommentCount", "ShareCount", "SaveCount", "ViewCount",
            "MyReaction", "IsSaved", "IsOwner", "User", "Media", "Hashtags");
        AssertProperties<PostDetailUserResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified");
        AssertProperties<PostDetailMediaResponse>("Id", "MediaType", "MediaUrl", "ThumbnailUrl");
        AssertProperties<PostDetailHashtagResponse>("Id", "Name");
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
        AssertProperties<DeletePostResponse>("Message");
        AssertProperties<ReportPostResponse>("Id", "Message");
        AssertProperties<MessageResponse>("Message");
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
