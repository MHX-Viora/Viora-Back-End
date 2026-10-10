using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/mini-apps")]
[Tags("Mini Apps")]
public sealed class MiniAppsController(IMiniAppService service, IMiniAppManagementService management) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<MiniAppListResponse> List(CancellationToken cancellationToken) => service.GetActiveAsync(cancellationToken);

    [HttpGet("categories")]
    [AllowAnonymous]
    public Task<IReadOnlyList<MiniAppCategoryDto>> Categories(CancellationToken cancellationToken) => management.GetCategoriesAsync(false, cancellationToken);

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

    [HttpPost("{id:guid}/sessions/validate")]
    [Authorize]
    [EnableRateLimiting("mini-app-launch")]
    public async Task<ActionResult<ValidateMiniAppSessionResponse>> Validate(Guid id, ValidateMiniAppSessionRequest request, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        return Ok(await service.ValidateSessionAsync(accountId, id, request, ct));
    }
    [HttpPost("{id:guid}/sessions/close")]
    [Authorize]
    public async Task<IActionResult> Close(Guid id, ValidateMiniAppSessionRequest request, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        await service.CloseSessionAsync(accountId, id, request, ct); return NoContent();
    }
    [HttpPost("{id:guid}/authorize")]
    [Authorize]
    [EnableRateLimiting("mini-app-launch")]
    public async Task<ActionResult<LaunchMiniAppResponse>> Authorize(Guid id, AuthorizeMiniAppRequest request, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        return Ok(await service.AuthorizeAsync(accountId, id, request, ct));
    }
    [HttpPost("{id:guid}/reports")]
    [Authorize]
    [EnableRateLimiting("mini-app-launch")]
    public async Task<IActionResult> Report(Guid id, MiniAppReportInput request, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId)) return Unauthorized();
        await service.ReportAsync(accountId, id, request, ct); return NoContent();
    }
}

public sealed record MiniAppErrorResponse(string Code, string Message);
