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
public sealed class MiniAppAuthController : ControllerBase
{
    [HttpPost("exchange")]
    public IActionResult Exchange() => StatusCode(410, new MiniAppErrorResponse("LEGACY_PROTOCOL_REMOVED", "Use the OpenID Connect authorization-code flow with PKCE S256."));
}
