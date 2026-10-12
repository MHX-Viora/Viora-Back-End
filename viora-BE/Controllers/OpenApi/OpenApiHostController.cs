using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Claims;
using Viora.Infrastructure.OpenApi;
using viora_BE.OpenApi;

namespace viora_BE.Controllers.OpenApi;

[ApiController, Authorize, Route("api/open-api"), EnableRateLimiting("mini-app-launch")]
public sealed class OpenApiHostController(OpenApiAccessService access, PartnerOpenApiOptions settings) : ControllerBase
{
    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize(OpenApiTransaction input, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var accountId)) return Unauthorized();
        var result = await access.Prepare(accountId, input, ct);
        string? authorizeUrl = result.Ticket is null ? null : QueryHelpers.AddQueryString(settings.Issuer.TrimEnd('/') + "/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = input.ClientId, ["redirect_uri"] = input.RedirectUri,
            ["state"] = input.State, ["nonce"] = input.Nonce, ["scope"] = string.Join(' ', input.Scopes), ["code_challenge"] = input.CodeChallenge,
            ["code_challenge_method"] = "S256", ["login_ticket"] = result.Ticket
        });
        return Ok(new { result.AppId, result.Name, result.IconUrl, result.RequiresConsent, result.Permissions, authorizeUrl, expiresIn = 60 });
    }
}
