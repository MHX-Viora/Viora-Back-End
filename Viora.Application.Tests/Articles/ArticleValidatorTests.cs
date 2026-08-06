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

    [Theory]
    [InlineData(ArticleBlockType.Heading)]
    [InlineData(ArticleBlockType.Text)]
    public void Create_requires_heading_and_text(ArticleBlockType existingType)
    {
        var blocks = new[] { Block(0, existingType, "Content") };
        var result = new CreateArticleValidator().Validate(
            new CreateArticleCommand(UserId, "Title", PostVisibility.Public, blocks));

        Assert.False(result.IsValid);
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
