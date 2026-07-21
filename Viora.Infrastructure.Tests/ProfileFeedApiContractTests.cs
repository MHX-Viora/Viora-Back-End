using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ProfileFeedApiContractTests
{
    [Fact]
    public void Profile_controller_exposes_authenticated_profile_feed_routes()
    {
        Assert.NotNull(typeof(ProfileController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/profile/me", typeof(ProfileController).GetCustomAttribute<RouteAttribute>()!.Template);
        AssertRoute(nameof(ProfileController.ReactedPosts), "reacted-posts");
        AssertRoute(nameof(ProfileController.ReactedReels), "reacted-reels");
        AssertRoute(nameof(ProfileController.SavedPosts), "saved-posts");
        AssertRoute(nameof(ProfileController.SavedReels), "saved-reels");
    }

    [Fact]
    public void Profile_feed_uses_existing_feed_response_contract()
    {
        Assert.Equal(typeof(PostFeedResponse), typeof(ProfileController)
            .GetMethod(nameof(ProfileController.ReactedPosts))!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(x => x.StatusCode == 200)
            .Type);
    }

    [Fact]
    public void Profile_feed_validator_rejects_invalid_page_size()
    {
        var validator = new GetProfileFeedValidator();
        var result = validator.Validate(new GetProfileFeedQuery(Guid.NewGuid(), ProfileFeedKind.SavedPosts, 0, 101));
        Assert.False(result.IsValid);
    }

    private static void AssertRoute(string actionName, string template)
    {
        var action = typeof(ProfileController).GetMethod(actionName)!;
        Assert.Equal(template, action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
