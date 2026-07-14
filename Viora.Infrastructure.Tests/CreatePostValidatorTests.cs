using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CreatePostValidatorTests
{
    private readonly CreatePostValidator validator = new();

    [Fact]
    public async Task Create_post_accepts_content_without_files()
    {
        var result = await validator.ValidateAsync(new CreatePostCommand(
            Guid.NewGuid(),
            "Hôm nay trời đẹp quá.",
            PostVisibility.Public,
            10.762622,
            106.660172,
            "Quận 1, TP. Hồ Chí Minh",
            null,
            []));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Create_post_accepts_image_without_content()
    {
        var result = await validator.ValidateAsync(new CreatePostCommand(
            Guid.NewGuid(),
            null,
            PostVisibility.Public,
            null,
            null,
            null,
            null,
            [Image("image/jpeg")]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Create_post_requires_content_or_image()
    {
        var result = await validator.ValidateAsync(new CreatePostCommand(
            Guid.NewGuid(),
            "   ",
            PostVisibility.Public,
            null,
            null,
            null,
            null,
            []));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Create_post_rejects_video_and_more_than_ten_images()
    {
        var video = await validator.ValidateAsync(new CreatePostCommand(
            Guid.NewGuid(),
            null,
            PostVisibility.Public,
            null,
            null,
            null,
            null,
            [Image("video/mp4")]));

        var tooMany = await validator.ValidateAsync(new CreatePostCommand(
            Guid.NewGuid(),
            null,
            PostVisibility.Public,
            null,
            null,
            null,
            null,
            Enumerable.Range(0, 11).Select(_ => Image("image/webp")).ToList()));

        Assert.False(video.IsValid);
        Assert.False(tooMany.IsValid);
    }

    private static CreatePostFile Image(string contentType) => new(
        new MemoryStream([1]),
        "image.jpg",
        contentType,
        1);
}
