using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Posts;

public sealed class GetCommunityPostsQueryTests
{
    [Fact]
    public void SupportsAnOptionalPostTypeFilter()
    {
        var query = new GetCommunityPostsQuery(1, 10, null, null, null, PostType.Article);

        Assert.Equal(PostType.Article, query.PostType);
    }

    [Fact]
    public void SupportsAnOptionalFeedSort()
    {
        var query = new GetCommunityPostsQuery(
            1, 10, null, null, null, PostType.Article, PostFeedSort.Trending);

        Assert.Equal(PostFeedSort.Trending, query.Sort);
    }

    [Fact]
    public void TrendingScoreRewardsFreshArticlesAndWeightedShares()
    {
        var fresh = ArticleTrendingRanking.CalculateScore(100, 10, 1);
        var stale = ArticleTrendingRanking.CalculateScore(500, 0, 168);

        Assert.True(fresh > stale);
    }

    [Fact]
    public void TrendingScoreClampsFuturePublicationAge()
    {
        var future = ArticleTrendingRanking.CalculateScore(100, 0, -2);
        var current = ArticleTrendingRanking.CalculateScore(100, 0, 0);

        Assert.Equal(current, future);
    }
}
