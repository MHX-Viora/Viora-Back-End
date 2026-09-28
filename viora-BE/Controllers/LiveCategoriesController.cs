using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/live-categories")]
public sealed class LiveCategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Active(CancellationToken cancellationToken) => Ok(await db.LiveCategories
        .AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
        .Select(x => new LiveCategoryDto(x.Id, x.Name, x.Slug, x.Icon, x.SortOrder, x.IsActive, db.Lives.Count(l => l.CategoryId == x.Id)))
        .ToListAsync(cancellationToken));

    [Authorize(Roles = "2")]
    [HttpGet("admin")]
    public async Task<IActionResult> All(CancellationToken cancellationToken) => Ok(await db.LiveCategories
        .AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
        .Select(x => new LiveCategoryDto(x.Id, x.Name, x.Slug, x.Icon, x.SortOrder, x.IsActive, db.Lives.Count(l => l.CategoryId == x.Id)))
        .ToListAsync(cancellationToken));

    [Authorize(Roles = "2")]
    [HttpPost]
    public async Task<IActionResult> Create(LiveCategoryBody body, CancellationToken cancellationToken)
    {
        var slug = body.Slug.Trim().ToLowerInvariant();
        if (await db.LiveCategories.AnyAsync(x => x.Slug == slug, cancellationToken)) return Conflict(new { code = "CATEGORY_SLUG_EXISTS" });
        var category = new LiveCategory { Name = body.Name.Trim(), Slug = slug, Icon = body.Icon?.Trim(), SortOrder = body.SortOrder, IsActive = body.IsActive };
        db.LiveCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/live-categories/{category.Id}", new LiveCategoryDto(category.Id, category.Name, category.Slug, category.Icon, category.SortOrder, category.IsActive, 0));
    }

    [Authorize(Roles = "2")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, LiveCategoryBody body, CancellationToken cancellationToken)
    {
        var category = await db.LiveCategories.FindAsync([id], cancellationToken);
        if (category is null) return NotFound();
        var slug = body.Slug.Trim().ToLowerInvariant();
        if (await db.LiveCategories.AnyAsync(x => x.Id != id && x.Slug == slug, cancellationToken)) return Conflict(new { code = "CATEGORY_SLUG_EXISTS" });
        category.Name = body.Name.Trim();
        category.Slug = slug;
        category.Icon = body.Icon?.Trim();
        category.SortOrder = body.SortOrder;
        category.IsActive = body.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        var liveCount = await db.Lives.CountAsync(x => x.CategoryId == id, cancellationToken);
        return Ok(new LiveCategoryDto(id, category.Name, slug, category.Icon, category.SortOrder, category.IsActive, liveCount));
    }

    [Authorize(Roles = "2")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var category = await db.LiveCategories.FindAsync([id], cancellationToken);
        if (category is null) return NotFound();
        if (await db.Lives.AnyAsync(x => x.CategoryId == id, cancellationToken)) return Conflict(new { code = "CATEGORY_IN_USE", message = "Danh mục đã được dùng; hãy tắt thay vì xóa." });
        db.LiveCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record LiveCategoryBody(
    [property: Required, StringLength(100, MinimumLength = 1)] string Name,
    [property: Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$"), StringLength(100)] string Slug,
    [property: StringLength(80)] string? Icon,
    int SortOrder,
    bool IsActive);

public sealed record LiveCategoryDto(Guid Id, string Name, string Slug, string? Icon, int SortOrder, bool IsActive, int LiveCount);
