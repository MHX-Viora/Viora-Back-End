namespace Viora.Infrastructure.Developers;

public sealed class MiniAppReviewOptions
{
    public const string SectionName = "MiniAppReview";
    public bool Enabled { get; set; }
    public int ReadLimitPerMinute { get; set; } = 120;
    public int MutationLimitPerMinute { get; set; } = 30;
    public int CredentialLimitPerMinute { get; set; } = 5;
}
