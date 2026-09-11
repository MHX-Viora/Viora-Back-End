using Viora.Application.Articles;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Articles;

public sealed class ArticleInteractionValidatorTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ArticleId = Guid.NewGuid();

    [Fact]
    public void View_accepts_bounded_read_progress()
    {
        var result = new RecordArticleInteractionValidator().Validate(
            new RecordArticleInteractionCommand(
                UserId, ArticleId, ArticleInteractionType.View, 120, 70));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(10, -1)]
    [InlineData(10, 101)]
    [InlineData(86401, 50)]
    public void View_rejects_invalid_read_progress(int duration, decimal percentage)
    {
        var result = new RecordArticleInteractionValidator().Validate(
            new RecordArticleInteractionCommand(
                UserId, ArticleId, ArticleInteractionType.View, duration, percentage));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Client_cannot_forge_like_comment_share_or_save_signals()
    {
        var validator = new RecordArticleInteractionValidator();

        foreach (var type in new[]
                 {
                     ArticleInteractionType.Like,
                     ArticleInteractionType.Comment,
                     ArticleInteractionType.Share,
                     ArticleInteractionType.Save
                 })
        {
            Assert.False(validator.Validate(
                new RecordArticleInteractionCommand(UserId, ArticleId, type, 0, 0)).IsValid);
        }
    }
}
