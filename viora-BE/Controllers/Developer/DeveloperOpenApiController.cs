using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Persistence;
using viora_BE.Controllers.OpenApi;
using viora_BE.OpenApi;

namespace viora_BE.Controllers.Developer;

[ApiController, Authorize, Route("api/developer/mini-apps")]
public sealed class DeveloperOpenApiController(AppDbContext db, PartnerOpenApiOptions settings) : ControllerBase
{
    [HttpGet("{id:guid}/open-api")]
    public async Task<ActionResult<OpenApiManagementView>> Get(Guid id, CancellationToken ct)
    {
        var accountId = Guid.Parse(User.FindFirstValue("sub")!);
        var app = await db.MiniApps.AsNoTracking().Include(a => a.Developer).Include(a => a.PermissionMappings).ThenInclude(m => m.Permission)
            .SingleOrDefaultAsync(a => a.Id == id && a.DeletedAt == null && (a.Developer.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == a.DeveloperId && m.AccountId == accountId)), ct);
        return app is null ? NotFound() : Ok(await OpenApiManagementViews.Build(db, settings, app, ct));
    }
}
