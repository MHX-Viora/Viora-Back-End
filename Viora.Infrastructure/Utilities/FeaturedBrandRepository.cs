using Microsoft.EntityFrameworkCore;
using Viora.Application.Utilities;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Utilities;

public sealed class FeaturedBrandRepository(AppDbContext db) : IFeaturedBrandRepository
{
    public async Task<IReadOnlyList<FeaturedBrandItem>> ListAsync(bool activeOnly, CancellationToken token) =>
        await db.Set<FeaturedBrand>().AsNoTracking().Where(x => !activeOnly || x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new FeaturedBrandItem(x.Id, x.Name, x.LogoUrl, x.SortOrder, x.IsActive, x.Version)).ToListAsync(token);

    public async Task<FeaturedBrandItem> SaveAsync(Guid? id, SaveFeaturedBrand input, CancellationToken token)
    {
        FeaturedBrandValidation.Validate(input);
        var entity = id.HasValue ? await db.Set<FeaturedBrand>().FindAsync([id.Value], token) : new FeaturedBrand();
        if (entity is null) throw new KeyNotFoundException();
        if (id.HasValue && entity.Version != input.Version) throw new FeaturedBrandConflictException();
        if (!id.HasValue && input.Version != 0) throw new ArgumentException("Phiên bản tạo mới phải bằng 0.");
        if (!id.HasValue) db.Add(entity);
        entity.Name = string.IsNullOrWhiteSpace(input.Name) ? "Thương hiệu" : input.Name.Trim(); entity.LogoUrl = input.LogoUrl;
        entity.SortOrder = input.SortOrder; entity.IsActive = input.IsActive;
        if (id.HasValue) entity.Version++;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new FeaturedBrandConflictException(); }
        return new(entity.Id, entity.Name, entity.LogoUrl, entity.SortOrder, entity.IsActive, entity.Version);
    }

    public async Task<bool> DeleteAsync(Guid id, long version, CancellationToken token)
    {
        var entity = await db.Set<FeaturedBrand>().FindAsync([id], token);
        if (entity is null) return false;
        if (entity.Version != version) throw new FeaturedBrandConflictException();
        db.Remove(entity);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new FeaturedBrandConflictException(); }
        return true;
    }
}
