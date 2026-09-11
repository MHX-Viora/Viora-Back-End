using MediatR;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Application.Articles;

public sealed record GetRecommendedArticlesQuery(
    Guid UserId,
    int Page,
    int PageSize,
    string? Keyword = null) : IRequest<PostFeedResponse>;

public interface IArticleRecommendationService
{
    Task<PostFeedResponse> GetAsync(
        Guid userId,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken);
}

public sealed class GetRecommendedArticlesHandler(IArticleRecommendationService service)
    : IRequestHandler<GetRecommendedArticlesQuery, PostFeedResponse>
{
    public Task<PostFeedResponse> Handle(
        GetRecommendedArticlesQuery request,
        CancellationToken cancellationToken) =>
        service.GetAsync(
            request.UserId,
            Math.Max(request.Page, 1),
            Math.Clamp(request.PageSize, 1, 100),
            string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim(),
            cancellationToken);
}

public sealed class ArticleRecommendationService(IPostFeedRepository repository)
    : IArticleRecommendationService
{
    public async Task<PostFeedResponse> GetAsync(
        Guid userId,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var response = await repository.GetCommunityPostsAsync(
            new GetCommunityPostsQuery(
                page,
                pageSize,
                keyword,
                null,
                userId,
                PostType.Article,
                PostFeedSort.Recommended),
            cancellationToken);

        return response with
        {
            Items = ArticleRecommendationScoring.Diversify(response.Items)
        };
    }
}

public sealed record ArticleRecommendationScore(
    double InterestScore,
    double EngagementScore,
    double FreshnessScore,
    double AuthorAffinityScore,
    double DiscoveryScore,
    double FinalScore);

public static class ArticleRecommendationScoring
{
    public const double InterestWeight = .40;
    public const double EngagementWeight = .25;
    public const double FreshnessWeight = .20;
    public const double AuthorAffinityWeight = .10;
    public const double DiscoveryWeight = .05;
    public const double ViewEngagementWeight = .5;
    public const double ReactionEngagementWeight = 2;
    public const double CommentEngagementWeight = 4;
    public const double ShareEngagementWeight = 6;
    public const double SaveEngagementWeight = 7;
    public const double EngagementNormalizationScale = 100;
    public const double FreshnessHalfLifeHours = 48;

    public static ArticleRecommendationScore Calculate(
        double interest,
        int viewCount,
        int reactionCount,
        int commentCount,
        int shareCount,
        int saveCount,
        double ageHours,
        double authorAffinity,
        double discovery)
    {
        var interestScore = Clamp01(interest);
        var rawEngagement = Math.Max(0,
            viewCount * ViewEngagementWeight +
            reactionCount * ReactionEngagementWeight +
            commentCount * CommentEngagementWeight +
            shareCount * ShareEngagementWeight +
            saveCount * SaveEngagementWeight);
        var engagementScore = 1 - Math.Exp(-rawEngagement / EngagementNormalizationScale);
        var freshnessScore = Math.Exp(-Math.Max(0, ageHours) / FreshnessHalfLifeHours);
        var authorAffinityScore = Clamp01(authorAffinity);
        var discoveryScore = Clamp01(discovery);
        var finalScore =
            interestScore * InterestWeight +
            engagementScore * EngagementWeight +
            freshnessScore * FreshnessWeight +
            authorAffinityScore * AuthorAffinityWeight +
            discoveryScore * DiscoveryWeight;

        return new ArticleRecommendationScore(
            interestScore,
            engagementScore,
            freshnessScore,
            authorAffinityScore,
            discoveryScore,
            Clamp01(finalScore));
    }

    public static IReadOnlyList<PostFeedItemResponse> Diversify(
        IReadOnlyList<PostFeedItemResponse> ranked)
    {
        var remaining = ranked.ToList();
        var result = new List<PostFeedItemResponse>(ranked.Count);

        while (remaining.Count > 0)
        {
            var nextIndex = 0;
            var repeatsAuthor = result.Count >= 2 &&
                result[^1].User.Id == result[^2].User.Id &&
                remaining[0].User.Id == result[^1].User.Id;
            var repeatsContent = result.Count > 0 &&
                IsNearDuplicate(result[^1].Content, remaining[0].Content);
            if (repeatsAuthor || repeatsContent)
            {
                var alternative = remaining.FindIndex(item =>
                    (!repeatsAuthor || item.User.Id != result[^1].User.Id) &&
                    (!repeatsContent || !IsNearDuplicate(result[^1].Content, item.Content)));
                if (alternative >= 0) nextIndex = alternative;
            }

            result.Add(remaining[nextIndex]);
            remaining.RemoveAt(nextIndex);
        }

        return result;
    }

    private static bool IsNearDuplicate(string? left, string? right)
    {
        var leftTerms = Terms(left);
        var rightTerms = Terms(right);
        if (leftTerms.Count < 3 || rightTerms.Count < 3) return false;

        var intersection = leftTerms.Intersect(rightTerms).Count();
        var union = leftTerms.Union(rightTerms).Count();
        return union > 0 && intersection / (double)union >= .75;
    }

    private static HashSet<string> Terms(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.ToLowerInvariant()
                .Split(value.Where(character => !char.IsLetterOrDigit(character))
                    .Distinct()
                    .ToArray(), StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Length >= 3)
                .ToHashSet(StringComparer.Ordinal);

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}
