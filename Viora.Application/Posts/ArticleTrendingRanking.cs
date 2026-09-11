namespace Viora.Application.Posts;

public static class ArticleTrendingRanking
{
    public const double ShareWeight = 10d;
    public const double TimeOffsetHours = 2d;
    public const double DecayExponent = 1.3d;

    public static double CalculateScore(
        int viewCount,
        int shareCount,
        double hoursSincePublished)
    {
        var age = Math.Max(hoursSincePublished, 0d);
        return (viewCount + shareCount * ShareWeight) /
            Math.Pow(age + TimeOffsetHours, DecayExponent);
    }
}
