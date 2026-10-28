using Microsoft.EntityFrameworkCore;
using Viora.Application.Utilities;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Utilities;

public sealed class UtilityBannerRepository(AppDbContext db) : IUtilityBannerRepository
{
    public async Task<IReadOnlyList<UtilityBannerItem>> ListAsync(bool activeOnly, CancellationToken token) =>
        await db.Set<UtilityBanner>().AsNoTracking().Where(x => !activeOnly || x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new UtilityBannerItem(x.Id, x.Title, x.ImageUrl, x.TargetUrl, x.SortOrder, x.IsActive, x.Version))
            .Take(activeOnly ? 20 : int.MaxValue).ToListAsync(token);

    public async Task<UtilityBannerItem> SaveAsync(Guid? id, SaveUtilityBanner input, CancellationToken token)
    {
        UtilityBannerValidation.Validate(input);
        var entity = id.HasValue ? await db.Set<UtilityBanner>().FindAsync([id.Value], token) : new UtilityBanner();
        if (entity is null) throw new KeyNotFoundException();
        if (id.HasValue && entity.Version != input.Version) throw new UtilityBannerConflictException();
        if (!id.HasValue && input.Version != 0) throw new ArgumentException("Phiên bản tạo mới phải bằng 0.");
        if (!id.HasValue) db.Add(entity);
        entity.Title = input.Title.Trim(); entity.ImageUrl = input.ImageUrl.Trim();
        entity.TargetUrl = string.IsNullOrWhiteSpace(input.TargetUrl) ? null : input.TargetUrl.Trim();
        entity.SortOrder = input.SortOrder; entity.IsActive = input.IsActive;
        if (id.HasValue) entity.Version++;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new UtilityBannerConflictException(); }
        return new(entity.Id, entity.Title, entity.ImageUrl, entity.TargetUrl, entity.SortOrder, entity.IsActive, entity.Version);
    }

    public async Task<bool> DeleteAsync(Guid id, long version, CancellationToken token)
    {
        var entity = await db.Set<UtilityBanner>().FindAsync([id], token);
        if (entity is null) return false;
        if (entity.Version != version) throw new UtilityBannerConflictException();
        db.Remove(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new UtilityBannerConflictException(); }
        return true;
    }
}
