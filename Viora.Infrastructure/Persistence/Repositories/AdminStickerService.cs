using Microsoft.EntityFrameworkCore;
using Viora.Application.Stickers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class AdminStickerService(AppDbContext db, IStickerMediaStorage storage) : IAdminStickerService
{
    public async Task<IReadOnlyList<AdminStickerPackResponse>> GetPacksAsync(CancellationToken token) =>
        await db.StickerPacks.AsNoTracking()
            .OrderBy(pack => pack.SortOrder).ThenBy(pack => pack.Name)
            .Select(pack => ToAdminResponse(pack,
                pack.Stickers.Count,
                pack.Owners.Count,
                pack.Stickers.SelectMany(sticker => sticker.Messages).Count()))
            .ToListAsync(token);

    public async Task<StickerPackDetailResponse?> GetPackAsync(Guid packId, CancellationToken token) =>
        await db.StickerPacks.AsNoTracking().Where(pack => pack.Id == packId)
            .Select(pack => new StickerPackDetailResponse(
                new StickerPackSummaryResponse(pack.Id, pack.Name, pack.Description, pack.ThumbnailUrl, pack.Price,
                    pack.Price == 0, pack.IsFeatured, pack.Stickers.Count, false, pack.Price == 0),
                false, pack.Price == 0,
                pack.Stickers.OrderBy(sticker => sticker.SortOrder).Select(sticker =>
                    new StickerResponse(sticker.Id, sticker.StickerPackId, sticker.Name, sticker.ImageUrl,
                        sticker.ThumbnailUrl, sticker.Format, sticker.SortOrder) { IsActive = sticker.IsActive }).ToList()))
            .FirstOrDefaultAsync(token);

    public async Task<AdminStickerPackResponse> CreatePackAsync(SaveStickerPackRequest request, CancellationToken token)
    {
        Validate(request);
        var now = DateTime.UtcNow;
        var pack = new StickerPack { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        Apply(pack, request);
        db.StickerPacks.Add(pack);
        await db.SaveChangesAsync(token);
        return ToAdminResponse(pack, 0, 0, 0);
    }

    public async Task<AdminStickerPackResponse?> UpdatePackAsync(Guid packId, SaveStickerPackRequest request, CancellationToken token)
    {
        Validate(request);
        var pack = await db.StickerPacks.FirstOrDefaultAsync(value => value.Id == packId, token);
        if (pack is null) return null;
        Apply(pack, request); pack.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        var counts = await GetCounts(packId, token);
        return ToAdminResponse(pack, counts.Stickers, counts.Owners, counts.Usage);
    }

    public async Task<bool> SetPackActiveAsync(Guid packId, bool isActive, CancellationToken token)
    {
        var pack = await db.StickerPacks.FirstOrDefaultAsync(value => value.Id == packId, token);
        if (pack is null) return false;
        pack.IsActive = isActive; pack.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(token); return true;
    }

    public async Task<StickerResponse?> CreateStickerAsync(Guid packId, SaveStickerRequest request, CancellationToken token)
    {
        Validate(request);
        if (!await db.StickerPacks.AnyAsync(pack => pack.Id == packId, token)) return null;
        var now = DateTime.UtcNow;
        var sticker = new Sticker { Id = Guid.NewGuid(), StickerPackId = packId, CreatedAt = now, UpdatedAt = now };
        Apply(sticker, request); db.Stickers.Add(sticker); await db.SaveChangesAsync(token); return ToResponse(sticker);
    }

    public async Task<StickerResponse?> UpdateStickerAsync(Guid stickerId, SaveStickerRequest request, CancellationToken token)
    {
        Validate(request);
        var sticker = await db.Stickers.FirstOrDefaultAsync(value => value.Id == stickerId, token);
        if (sticker is null) return null;
        Apply(sticker, request); sticker.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(token); return ToResponse(sticker);
    }

    public async Task<bool> SetStickerActiveAsync(Guid stickerId, bool isActive, CancellationToken token)
    {
        var sticker = await db.Stickers.FirstOrDefaultAsync(value => value.Id == stickerId, token);
        if (sticker is null) return false;
        sticker.IsActive = isActive; sticker.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(token); return true;
    }

    public async Task<string> UploadAsync(Guid packId, StickerUploadFile file, CancellationToken token)
    {
        if (file.Length <= 0 || file.Length > 5 * 1024 * 1024) throw new ArgumentException("Tep nhan dan phai nho hon 5 MB.");
        if (file.ContentType is not ("image/png" or "image/webp")) throw new ArgumentException("Chi chap nhan PNG hoac WebP.");
        if (!await db.StickerPacks.AsNoTracking().AnyAsync(pack => pack.Id == packId, token)) throw new ArgumentException("Bo nhan dan khong ton tai.");
        return await storage.UploadAsync(packId, file, token);
    }

    private async Task<(int Stickers, int Owners, int Usage)> GetCounts(Guid id, CancellationToken token) =>
        await db.StickerPacks.Where(pack => pack.Id == id).Select(pack => new ValueTuple<int, int, int>(
            pack.Stickers.Count, pack.Owners.Count, pack.Stickers.SelectMany(sticker => sticker.Messages).Count())).SingleAsync(token);

    private static void Validate(SaveStickerPackRequest value)
    {
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 120) throw new ArgumentException("Ten bo nhan dan khong hop le.");
        if (value.Price < 0) throw new ArgumentException("Gia khong duoc am.");
        if (!Uri.TryCreate(value.ThumbnailUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("ThumbnailUrl phai la HTTPS URL.");
        if (value.AvailableFrom.HasValue && value.AvailableUntil.HasValue && value.AvailableUntil <= value.AvailableFrom) throw new ArgumentException("Khoang thoi gian mo ban khong hop le.");
    }

    private static void Validate(SaveStickerRequest value)
    {
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 120) throw new ArgumentException("Ten nhan dan khong hop le.");
        if (!Enum.IsDefined(value.Format)) throw new ArgumentException("Dinh dang nhan dan khong hop le.");
        if (!Uri.TryCreate(value.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("ImageUrl phai la HTTPS URL.");
    }

    private static void Apply(StickerPack target, SaveStickerPackRequest source)
    { target.Name = source.Name.Trim(); target.Description = string.IsNullOrWhiteSpace(source.Description) ? null : source.Description.Trim(); target.ThumbnailUrl = source.ThumbnailUrl; target.Price = source.Price; target.IsFeatured = source.IsFeatured; target.IsActive = source.IsActive; target.SortOrder = source.SortOrder; target.AvailableFrom = source.AvailableFrom?.ToUniversalTime(); target.AvailableUntil = source.AvailableUntil?.ToUniversalTime(); }
    private static void Apply(Sticker target, SaveStickerRequest source)
    { target.Name = source.Name.Trim(); target.ImageUrl = source.ImageUrl; target.ThumbnailUrl = string.IsNullOrWhiteSpace(source.ThumbnailUrl) ? null : source.ThumbnailUrl; target.Format = source.Format; target.SortOrder = source.SortOrder; target.IsActive = source.IsActive; }
    private static StickerResponse ToResponse(Sticker value) => new(value.Id, value.StickerPackId, value.Name, value.ImageUrl, value.ThumbnailUrl, value.Format, value.SortOrder) { IsActive = value.IsActive };
    private static AdminStickerPackResponse ToAdminResponse(StickerPack value, int stickers, int owners, int usage) => new(value.Id, value.Name, value.Description, value.ThumbnailUrl, value.Price, value.IsFeatured, value.IsActive, value.SortOrder, value.AvailableFrom, value.AvailableUntil, stickers, owners, usage, value.CreatedAt, value.UpdatedAt);
}
