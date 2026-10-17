using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/me/miniapp-authorizations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SsoAuthorizationOwnerController(SsoAuthorizationOwnerService grants, SsoSessionService sessions) : ControllerBase
{
    private async Task<Guid?> Owner(CancellationToken ct)
    {
        if (User.FindFirst("token_type")?.Value != "access" || !Guid.TryParse(User.FindFirst("sub")?.Value, out var owner) ||
            !Guid.TryParse(User.FindFirst("sid")?.Value, out var social)) return null;
        try { await sessions.ValidateSocialSessionAsync(owner, social, ct); return owner; }
        catch (DeveloperAuthException) { return null; }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int offset = 0)
    {
        if (!ModelState.IsValid || offset < 0) return BadRequest(new { error = new { code = "SSO_INVALID_PAGE" } });
        return await Owner(ct) is { } owner ? Ok(await grants.ListAsync(owner, ct, offset)) : Unauthorized();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        if (await Owner(ct) is not { } owner) return Unauthorized();
        var grant = await grants.GetAsync(owner, id, ct); return grant == null ? NotFound() : Ok(grant);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        if (await Owner(ct) is not { } owner) return Unauthorized();
        try { return await grants.RevokeAsync(owner, id, ct) ? NoContent() : NotFound(); }
        catch (DbUpdateException) { return Conflict(new { error = new { code = "SSO_CONSENT_CONFLICT" } }); }
        catch (Npgsql.PostgresException e) when (e.SqlState is "40001" or "40P01") { return Conflict(new { error = new { code = "SSO_CONSENT_CONFLICT" } }); }
    }
}
