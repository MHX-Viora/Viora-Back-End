using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Viora.Application.Sharing;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ShareLinkApiContractTests
{
    [Fact]
    public void Share_link_response_contracts_match_fe_shape()
    {
        AssertProperties<ShareLinkResponse>("ShareUrl");
        AssertProperties<GroupShareLinkResponse>("InviteCode", "ShareUrl");
    }

    [Fact]
    public void Controllers_expose_common_share_link_routes()
    {
        AssertRoute<UserRelationshipsController, HttpGetAttribute>(nameof(UserRelationshipsController.GetUser), "{userId:guid}");
        AssertRoute<UserRelationshipsController, HttpGetAttribute>(nameof(UserRelationshipsController.GetUserShareLink), "{userId:guid}/share");
        AssertRoute<PostsController, HttpGetAttribute>(nameof(PostsController.GetShareLink), "{postId:guid}/share");
        AssertRoute<ReelsController, HttpGetAttribute>(nameof(ReelsController.Detail), "{reelId:guid}");
        AssertRoute<ReelsController, HttpGetAttribute>(nameof(ReelsController.GetShareLink), "{reelId:guid}/share");
        AssertRoute<ChatController, HttpGetAttribute>(nameof(ChatController.GetGroupShareLink), "groups/{conversationId:guid}/share");
        AssertRoute<ChatController, HttpGetAttribute>(nameof(ChatController.PreviewGroupByInviteCode), "groups/preview");
    }

    [Fact]
    public void Conversation_invite_code_is_required_stable_share_identifier()
    {
        var property = typeof(Conversation).GetProperty(nameof(Conversation.InviteCode))!;

        Assert.Equal(typeof(string), property.PropertyType);
    }

    private static void AssertRoute<TController, TAttribute>(string method, string template)
        where TAttribute : HttpMethodAttribute
    {
        var action = typeof(TController).GetMethod(method) ?? throw new InvalidOperationException(method);
        Assert.Equal(template, action.GetCustomAttribute<TAttribute>()!.Template);
    }

    private static void AssertProperties<T>(params string[] expected) =>
        Assert.Equal(expected.Order(), typeof(T).GetProperties().Select(x => x.Name).Order());
}
