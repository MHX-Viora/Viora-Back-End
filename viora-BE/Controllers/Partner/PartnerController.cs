using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Partner;

[ApiController]
[AllowAnonymous]
[Route("api/partner")]
[Tags("Partner")]
[EnableRateLimiting("mini-app-exchange")]
public sealed class PartnerController(IDeveloperMiniAppService service) : ControllerBase
{
    [HttpGet("mini-app")]
    public async Task<ActionResult<MiniAppDeveloperView>> Configuration(
        [FromHeader(Name = "X-Client-Id")] string clientId,
        [FromHeader(Name = "X-Client-Secret")] string clientSecret,
        CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetPartnerConfigurationAsync(clientId, clientSecret, cancellationToken)); }
        catch (MiniAppException exception) { return StatusCode(exception.StatusCode, new MiniAppErrorResponse(exception.Code, exception.Message)); }
    }
}
