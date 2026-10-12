namespace Viora.Application.MiniApps;

public sealed record MiniAppCatalogQuery(int Page = 1, int PageSize = 24, string? Search = null, Guid? CategoryId = null, string Rail = "all");
public sealed record MiniAppCatalogPage(IReadOnlyList<MiniAppListItemDto> Items, int Total, int Page, int PageSize, IReadOnlyList<Guid> FavoriteIds);
public sealed record MiniAppFavoriteInput(bool Favorite);
public sealed record MiniAppFavoriteOrder(IReadOnlyList<Guid> AppIds);
public interface IMiniAppCatalogService
{
    Task<MiniAppCatalogPage> GetCatalogAsync(Guid accountId, MiniAppCatalogQuery query, CancellationToken cancellationToken);
    Task SetFavoriteAsync(Guid accountId, Guid appId, bool favorite, CancellationToken cancellationToken);
    Task ReorderFavoritesAsync(Guid accountId, MiniAppFavoriteOrder input, CancellationToken cancellationToken);
    Task ClearHistoryAsync(Guid accountId, CancellationToken cancellationToken);
}
