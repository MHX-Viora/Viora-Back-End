using Viora.Application.Articles;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Articles;

public sealed class ArticleRecommendationTests
{
    [Fact]
    public void Score_rewards_interest_and_strong_engagement_without_raw_views_dominating()
    {
        var relevant = ArticleRecommendationScoring.Calculate(
            interest: 1, viewCount: 100, reactionCount: 20, commentCount: 10,
            shareCount: 8, saveCount: 12, ageHours: 6, authorAffinity: .8, discovery: .2);
        var viewsOnly = ArticleRecommendationScoring.Calculate(
            interest: 0, viewCount: 10_000, reactionCount: 0, commentCount: 0,
            shareCount: 0, saveCount: 0, ageHours: 168, authorAffinity: 0, discovery: 0);

        Assert.True(relevant.FinalScore > viewsOnly.FinalScore);
        Assert.InRange(relevant.FinalScore, 0, 1);
    }

    [Fact]
    public void Score_gives_new_articles_a_stronger_freshness_signal()
    {
        var fresh = ArticleRecommendationScoring.Calculate(0, 0, 0, 0, 0, 0, 1, 0, 1);
        var stale = ArticleRecommendationScoring.Calculate(0, 0, 0, 0, 0, 0, 240, 0, 1);

        Assert.True(fresh.FreshnessScore > stale.FreshnessScore);
        Assert.True(fresh.FinalScore > stale.FinalScore);
    }

    [Fact]
    public void Cold_start_score_uses_engagement_freshness_and_discovery_without_history()
    {
        var score = ArticleRecommendationScoring.Calculate(
            interest: 0,
            viewCount: 25,
            reactionCount: 5,
            commentCount: 2,
            shareCount: 1,
            saveCount: 1,
            ageHours: 12,
            authorAffinity: 0,
            discovery: 1);

        Assert.Equal(0, score.InterestScore);
        Assert.Equal(0, score.AuthorAffinityScore);
        Assert.True(score.EngagementScore > 0);
        Assert.True(score.FreshnessScore > 0);
        Assert.True(score.DiscoveryScore > 0);
        Assert.InRange(score.FinalScore, 0, 1);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, -10, 1, 1)]
    [InlineData(2, 500, 2, 100)]
    public async Task Handler_normalizes_page_and_page_size(
        int requestedPage,
        int requestedPageSize,
        int expectedPage,
        int expectedPageSize)
    {
        var service = new CapturingRecommendationService();
        var handler = new GetRecommendedArticlesHandler(service);

        await handler.Handle(
            new GetRecommendedArticlesQuery(Guid.NewGuid(), requestedPage, requestedPageSize),
            CancellationToken.None);

        Assert.Equal(expectedPage, service.Page);
        Assert.Equal(expectedPageSize, service.PageSize);
    }

    [Fact]
    public void Diversity_limits_consecutive_articles_from_the_same_author()
    {
        var authorA = Guid.NewGuid();
        var authorB = Guid.NewGuid();
        var ranked = new[]
        {
            Item(Guid.NewGuid(), authorA),
            Item(Guid.NewGuid(), authorA),
            Item(Guid.NewGuid(), authorA),
            Item(Guid.NewGuid(), authorB)
        };

        var diversified = ArticleRecommendationScoring.Diversify(ranked);

        Assert.Equal(authorB, diversified[2].User.Id);
    }

    [Fact]
    public void Diversity_separates_near_duplicate_titles_when_an_alternative_exists()
    {
        var ranked = new[]
        {
            Item(Guid.NewGuid(), Guid.NewGuid(), "Digital painting techniques for beginners"),
            Item(Guid.NewGuid(), Guid.NewGuid(), "Digital painting techniques for beginners today"),
            Item(Guid.NewGuid(), Guid.NewGuid(), "Sculpture exhibition opens in Hanoi")
        };

        var diversified = ArticleRecommendationScoring.Diversify(ranked);

        Assert.Equal("Sculpture exhibition opens in Hanoi", diversified[1].Content);
    }

    private static PostFeedItemResponse Item(Guid id, Guid authorId, string title = "Title") => new(
        id, title, PostType.Article, PostVisibility.Public, null, null,
        DateTime.UtcNow, new PostFeedUserResponse(authorId, "Author", null, false),
        [], 0, 0, 0, 0, 0, false, false, null, false, [], null);

    private sealed class CapturingRecommendationService : IArticleRecommendationService
    {
        public int Page { get; private set; }
        public int PageSize { get; private set; }

        public Task<PostFeedResponse> GetAsync(
            Guid userId,
            int page,
            int pageSize,
            string? keyword,
            CancellationToken cancellationToken)
        {
            Page = page;
            PageSize = pageSize;
            return Task.FromResult(new PostFeedResponse(page, pageSize, 0, 0, []));
        }
    }
}
