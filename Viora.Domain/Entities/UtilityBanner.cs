namespace Viora.Domain.Entities;

public sealed class UtilityBanner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string? TargetUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; } = 1;
}
