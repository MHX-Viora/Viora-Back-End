using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class MiniAppEditorialService(AppDbContext db) : IMiniAppEditorialService
{
    private static void ValidateWindow(DateTime starts, DateTime? ends, int order)
    {
        if (starts.Kind != DateTimeKind.Utc || ends is DateTime end && end.Kind != DateTimeKind.Utc || ends <= starts || order is < 0 or > 10000)
            throw new MiniAppException("VALIDATION_ERROR", "Use a valid UTC schedule and display order.", 422);
    }

    public async Task<IReadOnlyList<MiniAppBannerDto>> BannersAsync(bool admin, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.MiniAppBanners.AsNoTracking().Where(b => admin || (b.IsActive && b.StartsAt <= now && (b.EndsAt == null || b.EndsAt > now) &&
            ((b.MiniAppId != null && db.MiniApps.Any(a => a.Id == b.MiniAppId && a.DeletedAt == null && !a.HiddenFromCatalog && a.Status == MiniAppStatus.Active && a.Developer.Status == DeveloperStatus.Active && a.PublishedVersion > 0 && a.PublishedConfigurationJson != null && (a.CatalogCategoryId == null || db.MiniAppCategories.Any(c => c.Id == a.CatalogCategoryId && c.IsActive)))) ||
             (b.CategoryId != null && db.MiniAppCategories.Any(c => c.Id == b.CategoryId && c.IsActive)))))
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id).Take(admin ? 200 : 20)
            .Select(b => new MiniAppBannerDto(b.Id, b.Title, b.ImageUrl, b.MiniAppId, b.CategoryId, b.SortOrder, b.IsActive, b.StartsAt, b.EndsAt)).ToArrayAsync(ct);
    }

    public async Task<MiniAppBannerDto> SaveBannerAsync(Guid actorId, Guid? id, MiniAppBannerInput input, CancellationToken ct)
    {
        ValidateWindow(input.StartsAt, input.EndsAt, input.SortOrder);
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160 || input.ImageUrl.Length > 2048 ||
            !Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var image) || !MiniAppSecurityPolicy.IsAllowedHttpsUrl(input.ImageUrl, [image.IdnHost]) ||
            (input.MiniAppId == null) == (input.CategoryId == null))
            throw new MiniAppException("VALIDATION_ERROR", "Banner requires an HTTPS image and exactly one internal target.", 422);
        if (input.MiniAppId is Guid appId && !await db.MiniApps.AnyAsync(a => a.Id == appId && a.DeletedAt == null, ct) ||
            input.CategoryId is Guid categoryId && !await db.MiniAppCategories.AnyAsync(c => c.Id == categoryId, ct))
            throw new MiniAppException("TARGET_NOT_FOUND", "Banner target not found.", 404);
        var banner = id is null ? new MiniAppBanner() : await db.MiniAppBanners.SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new MiniAppException("NOT_FOUND", "Banner not found.", 404);
        if (id is null) { if (await db.MiniAppBanners.CountAsync(ct) >= 200) throw new MiniAppException("LIMIT_REACHED", "Maximum 200 banners.", 422); db.MiniAppBanners.Add(banner); }
        banner.Title = input.Title.Trim(); banner.ImageUrl = input.ImageUrl; banner.MiniAppId = input.MiniAppId; banner.CategoryId = input.CategoryId;
        banner.SortOrder = input.SortOrder; banner.IsActive = input.IsActive; banner.StartsAt = input.StartsAt; banner.EndsAt = input.EndsAt;
        Audit(actorId, "BannerSaved", banner.Id.ToString()); await db.SaveChangesAsync(ct);
        return new(banner.Id, banner.Title, banner.ImageUrl, banner.MiniAppId, banner.CategoryId, banner.SortOrder, banner.IsActive, banner.StartsAt, banner.EndsAt);
    }

    public async Task DeleteBannerAsync(Guid actorId, Guid id, CancellationToken ct)
    {
        var banner = await db.MiniAppBanners.SingleOrDefaultAsync(b => b.Id == id, ct);
        if (banner is null) return;
        db.MiniAppBanners.Remove(banner); Audit(actorId, "BannerDeleted", id.ToString()); await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MiniAppPlacementDto>> PlacementsAsync(CancellationToken ct) => await db.MiniAppPlacements.AsNoTracking()
        .OrderBy(p => p.SortOrder).Take(200).Select(p => new MiniAppPlacementDto(p.MiniAppId, p.MiniApp.CatalogName ?? p.MiniApp.Name, p.SortOrder, p.IsActive, p.StartsAt, p.EndsAt)).ToArrayAsync(ct);

    public async Task SavePlacementAsync(Guid actorId, Guid appId, MiniAppPlacementInput input, CancellationToken ct)
    {
        ValidateWindow(input.StartsAt, input.EndsAt, input.SortOrder);
        if (!await db.MiniApps.AnyAsync(a => a.Id == appId && a.DeletedAt == null && a.PublishedVersion > 0, ct)) throw new MiniAppException("NOT_FOUND", "Published app required.", 404);
        var placement = await db.MiniAppPlacements.SingleOrDefaultAsync(p => p.MiniAppId == appId, ct);
        if (placement is null) { placement = new MiniAppPlacement { MiniAppId = appId }; db.MiniAppPlacements.Add(placement); }
        placement.SortOrder = input.SortOrder; placement.IsActive = input.IsActive; placement.StartsAt = input.StartsAt; placement.EndsAt = input.EndsAt;
        Audit(actorId, "PlacementSaved", appId.ToString()); await db.SaveChangesAsync(ct);
    }

    public async Task SetVisibilityAsync(Guid actorId, Guid appId, bool hidden, CancellationToken ct)
    {
        var app = await db.MiniApps.SingleOrDefaultAsync(a => a.Id == appId && a.DeletedAt == null, ct) ?? throw new MiniAppException("NOT_FOUND", "App not found.", 404);
        app.HiddenFromCatalog = hidden; Audit(actorId, "CatalogVisibilityChanged", $"{appId}:{hidden}"); await db.SaveChangesAsync(ct);
    }
    private void Audit(Guid actorId, string action, string detail) => db.MiniAppAuditLogs.Add(new MiniAppAuditLog { ActorAccountId = actorId, Action = action, Detail = detail });
}
