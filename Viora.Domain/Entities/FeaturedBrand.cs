namespace Viora.Domain.Entities;

public sealed class FeaturedBrand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; } = 1;
}
