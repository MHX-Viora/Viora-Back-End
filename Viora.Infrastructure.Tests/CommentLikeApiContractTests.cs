using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CommentLikeApiContractTests
{
    [Fact]
    public void Comment_like_endpoint_contract_is_declared()
    {
        var method = typeof(PostsController).GetMethod(nameof(PostsController.ToggleCommentLike))!;

        Assert.NotNull(typeof(PostsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("/api/comments/{commentId:guid}/like", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(typeof(Task<ActionResult<CommentLikeApiResponse<CommentLikeResponse>>>), method.ReturnType);

        var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Order()
            .ToArray();
        Assert.Equal([200, 400, 401, 404], statuses);
    }

    [Fact]
    public void Comment_like_response_uses_required_wrapper_shape()
    {
        AssertProperties<CommentLikeApiResponse<CommentLikeResponse>>("Success", "Message", "Data");
        AssertProperties<CommentLikeResponse>("CommentId", "IsLiked", "LikeCount");
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
