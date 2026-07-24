using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
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

    [Fact]
    public void Profile_feed_queries_can_be_translated_by_postgresql_provider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation_only;Username=test;Password=test")
            .Options;
        using var db = new AppDbContext(options);
        var userId = Guid.NewGuid();

        var reactedSql = db.PostReactions.AsNoTracking()
            .Where(reaction => reaction.UserId == userId)
            .Where(reaction =>
                reaction.Post.PostType == PostType.Post &&
                reaction.Post.Status == PostStatus.Published &&
                reaction.Post.DeletedAt == null)
            .OrderByDescending(reaction => reaction.CreatedAt)
            .Select(reaction => new PostFeedItemResponse(
                reaction.Post.Id,
                reaction.Post.Content,
                reaction.Post.PostType,
                reaction.Post.Visibility,
                reaction.Post.Location,
                reaction.Post.Link,
                reaction.Post.CreatedAt,
                new PostFeedUserResponse(reaction.Post.User.Id, reaction.Post.User.DisplayName, reaction.Post.User.AvatarUrl, reaction.Post.User.IsVerified),
                reaction.Post.Media.Select(media => new PostFeedMediaResponse(media.Id, media.MediaUrl, media.ThumbnailUrl)).ToList(),
                reaction.Post.ReactionCount,
                reaction.Post.CommentCount,
                reaction.Post.ShareCount,
                reaction.Post.SaveCount,
                reaction.Post.ViewCount,
                reaction.Post.UserId == userId,
                true,
                (ReactionType?)reaction.ReactionType,
                db.SavedPosts.Any(saved => saved.PostId == reaction.Post.Id && saved.UserId == userId),
                db.PostHashtags.Where(postHashtag => postHashtag.PostId == reaction.Post.Id).Select(postHashtag => new PostDetailHashtagResponse(postHashtag.Hashtag.Id, postHashtag.Hashtag.Name)).ToList(),
                null))
            .ToQueryString();

        var savedSql = db.SavedPosts.AsNoTracking()
            .Where(saved => saved.UserId == userId)
            .Where(saved =>
                saved.Post.PostType == PostType.ShortVideo &&
                saved.Post.Status == PostStatus.Published &&
                saved.Post.DeletedAt == null)
            .OrderByDescending(saved => saved.CreatedAt)
            .Select(saved => new PostFeedItemResponse(
                saved.Post.Id,
                saved.Post.Content,
                saved.Post.PostType,
                saved.Post.Visibility,
                saved.Post.Location,
                saved.Post.Link,
                saved.Post.CreatedAt,
                new PostFeedUserResponse(saved.Post.User.Id, saved.Post.User.DisplayName, saved.Post.User.AvatarUrl, saved.Post.User.IsVerified),
                saved.Post.Media.Select(media => new PostFeedMediaResponse(media.Id, media.MediaUrl, media.ThumbnailUrl)).ToList(),
                saved.Post.ReactionCount,
                saved.Post.CommentCount,
                saved.Post.ShareCount,
                saved.Post.SaveCount,
                saved.Post.ViewCount,
                saved.Post.UserId == userId,
                db.PostReactions.Any(reaction => reaction.PostId == saved.Post.Id && reaction.UserId == userId),
                db.PostReactions.Where(reaction => reaction.PostId == saved.Post.Id && reaction.UserId == userId).Select(reaction => (ReactionType?)reaction.ReactionType).FirstOrDefault(),
                true,
                db.PostHashtags.Where(postHashtag => postHashtag.PostId == saved.Post.Id).Select(postHashtag => new PostDetailHashtagResponse(postHashtag.Hashtag.Id, postHashtag.Hashtag.Name)).ToList(),
                null))
            .ToQueryString();

        Assert.Contains("SELECT", reactedSql);
        Assert.Contains("SELECT", savedSql);
    }

    private static void AssertRoute(string actionName, string template)
    {
        var action = typeof(ProfileController).GetMethod(actionName)!;
        Assert.Equal(template, action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
