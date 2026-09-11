using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/mini-apps/{id:guid}/consent")]
[Tags("Mini App Consent")]
public sealed class MiniAppConsentController(IMiniAppService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Grant(Guid id, GrantConsentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        try { await service.GrantConsentAsync(accountId, id, request, cancellationToken); return NoContent(); }
        catch (MiniAppException exception) { return StatusCode(exception.StatusCode, new MiniAppErrorResponse(exception.Code, exception.Message)); }
    }

    [HttpDelete]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        await service.RevokeConsentAsync(accountId, id, cancellationToken);
        return NoContent();
    }

    private bool TryGetAccountId(out Guid accountId) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out accountId);
}
