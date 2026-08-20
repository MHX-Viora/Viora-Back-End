using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/mini-app-auth")]
[Tags("Mini App Authentication")]
[EnableRateLimiting("mini-app-exchange")]
public sealed class MiniAppAuthController(IMiniAppService service) : ControllerBase
{
    [HttpPost("exchange")]
    public async Task<ActionResult<ExchangeLaunchCodeResponse>> Exchange(ExchangeLaunchCodeRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await service.ExchangeAsync(request, cancellationToken)); }
        catch (MiniAppException exception) { return StatusCode(exception.StatusCode, new MiniAppErrorResponse(exception.Code, exception.Message)); }
    }
}
