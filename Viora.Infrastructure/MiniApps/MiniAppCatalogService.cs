using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class MiniAppCatalogService(AppDbContext db) : IMiniAppCatalogService
{
    public static string Fold(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
        .Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();

    private IQueryable<MiniApp> Visible() => db.MiniApps.Where(app => app.DeletedAt == null && !app.HiddenFromCatalog &&
        app.Status == MiniAppStatus.Active && app.Developer.Status == DeveloperStatus.Active &&
        app.PublishedVersion > 0 && app.PublishedConfigurationJson != null && app.CatalogName != null &&
        (app.CatalogCategoryId == null || db.MiniAppCategories.Any(category => category.Id == app.CatalogCategoryId && category.IsActive)));

    public async Task<MiniAppCatalogPage> GetCatalogAsync(Guid accountId, MiniAppCatalogQuery input, CancellationToken ct)
    {
        await RequireAccount(accountId, ct);
        if (input.Page is < 1 or > 10000 || input.PageSize is < 1 or > 60 || input.Search?.Length > 120 ||
            !new[] { "all", "new", "featured", "favorites", "recent", "popular", "recommended" }.Contains(input.Rail))
            throw new MiniAppException("VALIDATION_ERROR", "Bộ lọc hoặc phân trang không hợp lệ.", 422);
        var query = Visible();
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var clearedBefore = await db.MiniAppHistoryStates.Where(state => state.AccountId == accountId).Select(state => (DateTime?)state.ClearedBefore).SingleOrDefaultAsync(ct) ?? DateTime.MinValue;
        // Bound discovery history without deleting security/audit records.
        if (clearedBefore < now.AddDays(-90)) clearedBefore = now.AddDays(-90);
        var recent = db.MiniAppLaunchLogs.Where(log => log.AccountId == accountId && log.Status == MiniAppLaunchStatus.Succeeded && log.CreatedAt > clearedBefore);
        var popular = db.MiniAppLaunchLogs.Where(log => log.Status == MiniAppLaunchStatus.Succeeded && log.CreatedAt >= weekAgo);
        var placements = db.MiniAppPlacements.Where(placement => placement.IsActive && placement.StartsAt <= now && (placement.EndsAt == null || placement.EndsAt > now));
        foreach (var term in Fold(input.Search?.Trim() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct())
            query = query.Where(app => app.CatalogSearchText.Contains(term));
        if (input.CategoryId is Guid categoryId) query = query.Where(app => app.CatalogCategoryId == categoryId);
        if (input.Rail == "favorites") query = query.Where(app => db.MiniAppFavorites.Any(favorite => favorite.AccountId == accountId && favorite.MiniAppId == app.Id));
        if (input.Rail == "featured") query = query.Where(app => placements.Any(placement => placement.MiniAppId == app.Id));
        if (input.Rail == "recent") query = query.Where(app => recent.Any(log => log.MiniAppId == app.Id));
        if (input.Rail == "popular") query = query.Where(app => popular.Any(log => log.MiniAppId == app.Id));
        // Recommendations only use the current account's category history, with real popularity as a fallback.
        var recentIds = input.Rail == "recommended" ? await recent.OrderByDescending(log => log.CreatedAt).Take(20).Select(log => log.MiniAppId).ToArrayAsync(ct) : [];
        var recommendedCategories = await Visible().Where(app => recentIds.Contains(app.Id) && app.CatalogCategoryId != null).Select(app => app.CatalogCategoryId).Distinct().ToArrayAsync(ct);
        var total = await query.CountAsync(ct);
        var ordered = input.Rail switch
        {
            "new" => query.OrderByDescending(app => app.PublishedAt).ThenBy(app => app.Id),
            "favorites" => query.OrderBy(app => db.MiniAppFavorites.Where(favorite => favorite.AccountId == accountId && favorite.MiniAppId == app.Id).Select(favorite => favorite.SortOrder).First()).ThenBy(app => app.Id),
            "recent" => query.OrderByDescending(app => recent.Where(log => log.MiniAppId == app.Id).Max(log => log.CreatedAt)).ThenBy(app => app.Id),
            "featured" => query.OrderBy(app => placements.Where(placement => placement.MiniAppId == app.Id).Select(placement => placement.SortOrder).First()).ThenBy(app => app.Id),
            "popular" => query.OrderByDescending(app => popular.Where(log => log.MiniAppId == app.Id).Select(log => log.AccountId).Distinct().Count()).ThenBy(app => app.Id),
            "recommended" => query.OrderByDescending(app => recommendedCategories.Contains(app.CatalogCategoryId)).ThenByDescending(app => popular.Where(log => log.MiniAppId == app.Id).Select(log => log.AccountId).Distinct().Count()).ThenBy(app => app.Id),
            _ => query.OrderBy(app => app.CatalogName).ThenBy(app => app.Id)
        };
        var apps = await ordered.Include(app => app.Developer).AsNoTracking().Skip((input.Page - 1) * input.PageSize).Take(input.PageSize).ToListAsync(ct);
        var categoryIds = apps.Select(app => app.CatalogCategoryId).OfType<Guid>().Distinct().ToArray();
        var categories = await db.MiniAppCategories.Where(category => categoryIds.Contains(category.Id)).ToDictionaryAsync(category => category.Id, category => category.Name, ct);
        var ids = apps.Select(app => app.Id).ToArray();
        var favorites = await db.MiniAppFavorites.Where(favorite => favorite.AccountId == accountId && ids.Contains(favorite.MiniAppId)).Select(favorite => favorite.MiniAppId).ToArrayAsync(ct);
        return new(apps.Select(app => {
            var snapshot = MiniAppConfigurationSnapshot.Published(app);
            return new MiniAppListItemDto(app.Id, snapshot.Name, snapshot.Slug, snapshot.Description, snapshot.IconUrl, snapshot.CoverUrl,
                snapshot.IsFeatured, app.Developer.Name, snapshot.CategoryId, snapshot.CategoryId is Guid id ? categories.GetValueOrDefault(id) : null,
                snapshot.AuthenticationMode, app.PublishedVersion, snapshot.AuthenticationMode == "AnktSso" && snapshot.AutoLogin);
        }).ToArray(), total, input.Page, input.PageSize, favorites);
    }

    public Task SetFavoriteAsync(Guid accountId, Guid appId, bool favorite, CancellationToken ct) => WithAccountLock(accountId, () => SetFavoriteCore(accountId, appId, favorite, ct), ct);
    private async Task SetFavoriteCore(Guid accountId, Guid appId, bool favorite, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(account => account.Id == accountId && account.Status == AccountStatus.Active && account.DeletedAt == null, ct))
            throw new MiniAppException("ACCOUNT_NOT_ACTIVE", "Tài khoản không hoạt động.", 403);
        var existing = await db.MiniAppFavorites.SingleOrDefaultAsync(item => item.AccountId == accountId && item.MiniAppId == appId, ct);
        if (!favorite)
        {
            if (existing is not null) { db.MiniAppFavorites.Remove(existing); await db.SaveChangesAsync(ct); }
            return;
        }
        if (!await Visible().AnyAsync(app => app.Id == appId, ct)) throw new MiniAppException(MiniAppErrorCodes.NotFound, "Ứng dụng không khả dụng.", 404);
        if (existing is not null) return;
        if (await db.MiniAppFavorites.CountAsync(item => item.AccountId == accountId, ct) >= 100)
            throw new MiniAppException("FAVORITES_LIMIT", "Bạn có thể ghim tối đa 100 ứng dụng.", 422);
        var order = await db.MiniAppFavorites.Where(item => item.AccountId == accountId).Select(item => (int?)item.SortOrder).MaxAsync(ct) ?? -1;
        db.MiniAppFavorites.Add(new MiniAppFavorite { AccountId = accountId, MiniAppId = appId, SortOrder = order + 1 });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) {
            db.ChangeTracker.Clear();
            if (!await db.MiniAppFavorites.AnyAsync(item => item.AccountId == accountId && item.MiniAppId == appId, ct)) throw;
        }
    }

    public Task ReorderFavoritesAsync(Guid accountId, MiniAppFavoriteOrder input, CancellationToken ct) => WithAccountLock(accountId, () => ReorderCore(accountId, input, ct), ct);
    private async Task ReorderCore(Guid accountId, MiniAppFavoriteOrder input, CancellationToken ct)
    {
        await RequireAccount(accountId, ct);
        if (input.AppIds is null || input.AppIds.Count > 100 || input.AppIds.Distinct().Count() != input.AppIds.Count)
            throw new MiniAppException("VALIDATION_ERROR", "Thứ tự ứng dụng không hợp lệ.", 422);
        var favorites = await db.MiniAppFavorites.Where(item => item.AccountId == accountId).ToListAsync(ct);
        var favoriteIds = favorites.Select(f => f.MiniAppId).ToArray();
        var visibleIds = await Visible().Where(app => favoriteIds.Contains(app.Id)).Select(app => app.Id).ToArrayAsync(ct);
        if (visibleIds.Length != input.AppIds.Count || visibleIds.Any(id => !input.AppIds.Contains(id)))
            throw new MiniAppException("FAVORITES_CHANGED", "Danh sách yêu thích đã thay đổi. Hãy tải lại.", 409);
        var indices = input.AppIds.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index);
        var hiddenOrder = indices.Count;
        foreach (var favorite in favorites.OrderBy(f => f.SortOrder).ThenBy(f => f.Id)) favorite.SortOrder = indices.TryGetValue(favorite.MiniAppId, out var index) ? index : hiddenOrder++;
        await db.SaveChangesAsync(ct);
    }

    private async Task RequireAccount(Guid accountId, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(account => account.Id == accountId && account.Status == AccountStatus.Active && account.DeletedAt == null, ct))
            throw new MiniAppException("ACCOUNT_NOT_ACTIVE", "Active account required.", 403);
    }

    public Task ClearHistoryAsync(Guid accountId, CancellationToken ct) => WithAccountLock(accountId, () => ClearHistoryCore(accountId, ct), ct);
    private async Task ClearHistoryCore(Guid accountId, CancellationToken ct)
    {
        await RequireAccount(accountId, ct);
        var state = await db.MiniAppHistoryStates.SingleOrDefaultAsync(state => state.AccountId == accountId, ct);
        if (state is null) { state = new MiniAppHistoryState { AccountId = accountId }; db.MiniAppHistoryStates.Add(state); }
        state.ClearedBefore = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task WithAccountLock(Guid accountId, Func<Task> operation, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) { await operation(); return; }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize preferences across API instances; the account hash is a SQL parameter.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))", ct);
        await operation(); await transaction.CommitAsync(ct);
    }
}
