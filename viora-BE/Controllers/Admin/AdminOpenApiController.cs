using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.OpenApi;
using Viora.Infrastructure.Persistence;
using viora_BE.Controllers.OpenApi;
using viora_BE.OpenApi;

namespace viora_BE.Controllers.Admin;

[ApiController, Authorize(Roles = "2"), Route("api/admin/mini-apps")]
public sealed class AdminOpenApiController(AppDbContext db, PartnerOpenApiOptions settings) : ControllerBase
{
    [HttpGet("{id:guid}/open-api")]
    public async Task<ActionResult<OpenApiManagementView>> Get(Guid id, CancellationToken ct)
    {
        var app = await db.MiniApps.AsNoTracking().Include(a => a.Developer).Include(a => a.PermissionMappings).ThenInclude(m => m.Permission).SingleOrDefaultAsync(a => a.Id == id && a.DeletedAt == null, ct);
        return app is null ? NotFound() : Ok(await OpenApiManagementViews.Build(db, settings, app, ct));
    }

    [HttpPost("{id:guid}/open-api/revoke-tokens")]
    public async Task<IActionResult> Revoke(Guid id, RevokeOpenApiTokensRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length < 3) return BadRequest(new { code = "INVALID_REASON", message = "Nhập lý do thu hồi từ 3 ký tự." });
        if (!settings.Enabled) return Conflict(new { code = "OPEN_API_DISABLED", message = "Open API chưa được bật." });
        if (!await db.MiniApps.AnyAsync(a => a.Id == id && a.DeletedAt == null, ct)) return NotFound();
        await HttpContext.RequestServices.GetRequiredService<OpenApiAccessService>().RevokeClient(id, ct);
        db.MiniAppAuditLogs.Add(new() { MiniAppId = id, ActorAccountId = Guid.Parse(User.FindFirstValue("sub")!), Action = "OpenApiAdminRevocation", Detail = JsonSerializer.Serialize(new { reason = input.Reason.Trim() }) });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record RevokeOpenApiTokensRequest([Required, StringLength(1000, MinimumLength = 3)] string Reason);
