using Viora.Application.Articles;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Articles;

public sealed class ArticleValidatorTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_rejects_article_without_blocks()
    {
        var result = new CreateArticleValidator().Validate(
            new CreateArticleCommand(UserId, "Title", PostVisibility.Public, []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorCode == "ARTICLE_BLOCKS_REQUIRED");
    }

    [Fact]
    public void Create_accepts_text_and_image_without_a_heading_block()
    {
        var blocks = new[]
        {
            Block(0, ArticleBlockType.Image, mediaUrl: "https://cdn.example.com/image.jpg"),
            Block(1, ArticleBlockType.Text, "Content")
        };
        var result = new CreateArticleValidator().Validate(
            new CreateArticleCommand(UserId, "Title", PostVisibility.Public, blocks));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_accepts_an_image_as_the_only_body_block()
    {
        var result = new CreateArticleValidator().Validate(new CreateArticleCommand(
            UserId, "Title", PostVisibility.Public,
            [Block(0, ArticleBlockType.Image, mediaUrl: "https://cdn.example.com/image.jpg")]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_accepts_an_image_as_the_only_body_block()
    {
        var image = new UpdateArticleBlockRequest(null, 0, ArticleBlockType.Image, null,
            "https://cdn.example.com/image.jpg", null, null);
        var result = new UpdateArticleValidator().Validate(new UpdateArticleCommand(
            UserId, Guid.NewGuid(), "Title", PostVisibility.Public, [image]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_rejects_duplicate_order_indexes()
    {
        var blocks = new[]
        {
            Block(0, ArticleBlockType.Heading, "Heading"),
            Block(0, ArticleBlockType.Text, "Text")
        };

        var result = new CreateArticleValidator().Validate(
            new CreateArticleCommand(UserId, "Title", PostVisibility.Public, blocks));

        Assert.Contains(result.Errors, error => error.ErrorCode == "ARTICLE_BLOCK_ORDER_INVALID");
    }

    [Fact]
    public void Create_accepts_a_valid_article()
    {
        var blocks = new[]
        {
            Block(0, ArticleBlockType.Heading, "Heading"),
            Block(1, ArticleBlockType.Text, "Text"),
            Block(2, ArticleBlockType.Image, mediaUrl: "https://cdn.example.com/image.jpg")
        };

        var result = new CreateArticleValidator().Validate(
            new CreateArticleCommand(UserId, "Article title", PostVisibility.Public, blocks));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Reading_time_rounds_up_and_never_returns_zero()
    {
        Assert.Equal(1, ArticleReadingTime.Calculate([]));
        Assert.Equal(2, ArticleReadingTime.Calculate([string.Join(' ', Enumerable.Repeat("word", 201))]));
    }

    private static CreateArticleBlockRequest Block(
        int order,
        ArticleBlockType type,
        string? content = null,
        string? mediaUrl = null) => new(order, type, content, mediaUrl, null, null);
}
