namespace Viora.Domain.Entities;

public sealed class MiniAppFavorite : CreatedEntity
{
    public Guid AccountId { get; set; }
    public Guid MiniAppId { get; set; }
    public int SortOrder { get; set; }
    public Account Account { get; set; } = null!;
    public MiniApp MiniApp { get; set; } = null!;
}

public sealed class MiniAppBanner : AuditableEntity
{
    public string Title { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public Guid? MiniAppId { get; set; }
    public Guid? CategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}

public sealed class MiniAppPlacement : AuditableEntity
{
    public Guid MiniAppId { get; set; }
    public MiniApp MiniApp { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}
