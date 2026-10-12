namespace Viora.Application.MiniApps;

public sealed record MiniAppBannerInput(string Title, string ImageUrl, Guid? MiniAppId, Guid? CategoryId, int SortOrder,
    bool IsActive, DateTime StartsAt, DateTime? EndsAt);
public sealed record MiniAppBannerDto(Guid Id, string Title, string ImageUrl, Guid? MiniAppId, Guid? CategoryId, int SortOrder,
    bool IsActive, DateTime StartsAt, DateTime? EndsAt);
public sealed record MiniAppPlacementInput(int SortOrder, bool IsActive, DateTime StartsAt, DateTime? EndsAt);
public sealed record MiniAppPlacementDto(Guid MiniAppId, string Name, int SortOrder, bool IsActive, DateTime StartsAt, DateTime? EndsAt);
public sealed record MiniAppVisibilityInput(bool Hidden);
public interface IMiniAppEditorialService
{
    Task<IReadOnlyList<MiniAppBannerDto>> BannersAsync(bool admin, CancellationToken ct);
    Task<MiniAppBannerDto> SaveBannerAsync(Guid actorId, Guid? id, MiniAppBannerInput input, CancellationToken ct);
    Task DeleteBannerAsync(Guid actorId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<MiniAppPlacementDto>> PlacementsAsync(CancellationToken ct);
    Task SavePlacementAsync(Guid actorId, Guid appId, MiniAppPlacementInput input, CancellationToken ct);
    Task SetVisibilityAsync(Guid actorId, Guid appId, bool hidden, CancellationToken ct);
}
