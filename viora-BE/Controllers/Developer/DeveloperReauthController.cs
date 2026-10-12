using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController, Authorize, Route("api/developer/reauth"), EnableRateLimiting("mini-app-launch")]
public sealed class DeveloperReauthController(IDeveloperReauthenticationService reauthentication) : ControllerBase
{
    [HttpPost]
    public Task<DeveloperReauthResponse> Verify(DeveloperReauthInput input, CancellationToken ct) => reauthentication.VerifyAsync(Guid.Parse(User.FindFirstValue("sub")!), input, ct);
}
