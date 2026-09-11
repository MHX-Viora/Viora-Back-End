using Microsoft.EntityFrameworkCore;
using Viora.Application.Stickers;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class StickerService(AppDbContext db) : IStickerService
{
    public async Task<StickerPackListResponse> GetPacksAsync(
        Guid userId,
        StickerPackFilter filter,
        int page,
        int pageSize,
        CancellationToken token)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var now = DateTime.UtcNow;
        var packs = db.StickerPacks.AsNoTracking().Where(pack =>
            pack.IsActive &&
            (!pack.AvailableFrom.HasValue || pack.AvailableFrom <= now) &&
            (!pack.AvailableUntil.HasValue || pack.AvailableUntil > now));

        packs = filter switch
        {
            StickerPackFilter.Featured => packs.Where(pack => pack.IsFeatured),
            StickerPackFilter.Free => packs.Where(pack => pack.Price == 0),
            StickerPackFilter.Paid => packs.Where(pack => pack.Price > 0),
            StickerPackFilter.Owned => packs.Where(pack => pack.Owners.Any(owner => owner.UserId == userId)),
            StickerPackFilter.Usable => packs.Where(pack => pack.Price == 0 || pack.Owners.Any(owner => owner.UserId == userId)),
            _ => packs
        };

        var totalItems = await packs.CountAsync(token);
        var items = await packs
            .OrderByDescending(pack => pack.IsFeatured)
            .ThenByDescending(pack => pack.CreatedAt)
            .ThenBy(pack => pack.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(pack => new StickerPackSummaryResponse(
                pack.Id,
                pack.Name,
                pack.Description,
                pack.ThumbnailUrl,
                pack.Price,
                pack.Price == 0,
                pack.IsFeatured,
                pack.Stickers.Count(sticker => sticker.IsActive),
                pack.Owners.Any(owner => owner.UserId == userId),
                pack.Price == 0 || pack.Owners.Any(owner => owner.UserId == userId)))
            .ToListAsync(token);

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new StickerPackListResponse(page, pageSize, totalItems, totalPages, items);
    }

    public async Task<StickerPackDetailResponse?> GetPackAsync(
        Guid userId,
        Guid packId,
        CancellationToken token)
    {
        var now = DateTime.UtcNow;
        return await db.StickerPacks
            .AsNoTracking()
            .Where(pack =>
                pack.Id == packId &&
                pack.IsActive &&
                (!pack.AvailableFrom.HasValue || pack.AvailableFrom <= now) &&
                (!pack.AvailableUntil.HasValue || pack.AvailableUntil > now))
            .Select(pack => new StickerPackDetailResponse(
                new StickerPackSummaryResponse(
                    pack.Id,
                    pack.Name,
                    pack.Description,
                    pack.ThumbnailUrl,
                    pack.Price,
                    pack.Price == 0,
                    pack.IsFeatured,
                    pack.Stickers.Count(sticker => sticker.IsActive),
                    pack.Owners.Any(owner => owner.UserId == userId),
                    pack.Price == 0 || pack.Owners.Any(owner => owner.UserId == userId)),
                pack.Owners.Any(owner => owner.UserId == userId),
                pack.Price == 0 || pack.Owners.Any(owner => owner.UserId == userId),
                pack.Stickers
                    .Where(sticker => sticker.IsActive)
                    .OrderBy(sticker => sticker.SortOrder)
                    .ThenBy(sticker => sticker.Name)
                    .Select(sticker => new StickerResponse(
                        sticker.Id,
                        sticker.StickerPackId,
                        sticker.Name,
                        sticker.ImageUrl,
                        sticker.ThumbnailUrl,
                        sticker.Format,
                        sticker.SortOrder))
                    .ToList()))
            .FirstOrDefaultAsync(token);
    }
}
