using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/mini-apps")]
[Tags("Mini Apps")]
public sealed class MiniAppsController(IMiniAppService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<MiniAppListResponse> List(CancellationToken cancellationToken) => service.GetActiveAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<MiniAppDetailDto>> Detail(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetActiveDetailAsync(id, cancellationToken);
        return result is null ? NotFound(new MiniAppErrorResponse(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.")) : Ok(result);
    }

    [HttpPost("{id:guid}/launch")]
    [Authorize]
    [EnableRateLimiting("mini-app-launch")]
    public async Task<ActionResult<LaunchMiniAppResponse>> Launch(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        try { return Ok(await service.LaunchAsync(accountId, id, cancellationToken)); }
        catch (MiniAppException exception) { return StatusCode(exception.StatusCode, new MiniAppErrorResponse(exception.Code, exception.Message)); }
    }

    private bool TryGetAccountId(out Guid accountId) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out accountId);
}

public sealed record MiniAppErrorResponse(string Code, string Message);
