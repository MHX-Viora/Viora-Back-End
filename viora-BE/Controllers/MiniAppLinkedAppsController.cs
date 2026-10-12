using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace viora_BE.Controllers;

public sealed record LinkedMiniAppDto(Guid Id, string Name, string? IconUrl, string Status, bool Available, string[] Permissions);

[ApiController, Authorize, Route("api/mini-apps/linked")]
public sealed class MiniAppLinkedAppsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var accountId) ||
            !await db.Accounts.AnyAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.DeletedAt == null, ct)) return Unauthorized();
        // Include suspended/removed applications: users must still be able to revoke them.
        var consents = await db.MiniAppUserConsents.AsNoTracking().Where(c => c.AccountId == accountId && c.Granted)
            .Include(c => c.MiniApp).ThenInclude(a => a.Developer).Include(c => c.Permission).ToListAsync(ct);
        var items = consents.GroupBy(c => c.MiniAppId).Select(group =>
        {
            var app = group.First().MiniApp;
            var name = app.Name; var icon = app.IconUrl;
            if (!string.IsNullOrEmpty(app.PublishedConfigurationJson))
            {
                try
                {
                    using var snapshot = JsonDocument.Parse(app.PublishedConfigurationJson);
                    if (snapshot.RootElement.TryGetProperty("Name", out var publishedName) && publishedName.ValueKind == JsonValueKind.String) name = publishedName.GetString() ?? name;
                    if (snapshot.RootElement.TryGetProperty("IconUrl", out var publishedIcon)) icon = publishedIcon.ValueKind == JsonValueKind.String ? publishedIcon.GetString() : null;
                }
                catch (JsonException) { /* Keep safe registry metadata for a revocable entry. */ }
            }
            return new LinkedMiniAppDto(app.Id, name, icon, app.DeletedAt != null ? "Deleted" : app.Status.ToString(),
                app.DeletedAt == null && app.Status == MiniAppStatus.Active && app.Developer.Status == DeveloperStatus.Active,
                group.Select(c => c.Permission.Code).Distinct().Order().ToArray());
        }).OrderBy(a => a.Name).ToArray();
        return Ok(new { items });
    }
}
