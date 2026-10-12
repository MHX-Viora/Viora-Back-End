using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using Viora.Application.MiniApps;
using Viora.Infrastructure.OpenApi;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace viora_BE.Controllers.OpenApi;

[ApiController, Authorize(Policy = "OpenApi"), EnableRateLimiting("open-api")]
public sealed class OpenApiResourcesController(OpenApiAccessService access, IMiniAppService runtime) : ControllerBase
{
    private async Task<OpenApiIdentity?> Identity(CancellationToken ct) => await access.Validate(User, ct);
    [HttpGet("~/v1/me"), HttpGet("~/oidc/userinfo")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var identity = await Identity(ct); if (identity is null) return Unauthorized();
        if (!User.HasScope("openid")) return StatusCode(403);
        var result = new Dictionary<string, object?> { ["sub"] = identity.Link.Subject };
        if (User.HasScope("profile")) { result["name"] = identity.Account.User?.DisplayName; result["picture"] = identity.Account.User?.AvatarUrl; }
        if (User.HasScope("email")) result["email"] = identity.Account.Email;
        if (User.HasScope("phone")) result["phone_number"] = identity.Account.Phone;
        return Ok(result);
    }
    [HttpGet("~/v1/me/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var identity = await Identity(ct); if (identity is null) return Unauthorized();
        if (!User.HasScope("profile")) return StatusCode(403);
        return Ok(new { sub = identity.Link.Subject, name = identity.Account.User?.DisplayName, picture = identity.Account.User?.AvatarUrl });
    }
    [HttpGet("~/v1/apps/{appId:guid}/permissions")]
    public async Task<IActionResult> Permissions(Guid appId, CancellationToken ct)
    {
        var identity = await Identity(ct); if (identity is null) return Unauthorized();
        if (identity.App.Id != appId || !User.HasScope("openid")) return StatusCode(403);
        return Ok(new { appId, clientId = identity.App.ClientId, scopes = identity.Scopes });
    }
    [HttpPost("~/v1/account-links")]
    public async Task<IActionResult> Link(CancellationToken ct)
    {
        var identity = await Identity(ct); if (identity is null) return Unauthorized();
        if (!User.HasScope("openid")) return StatusCode(403);
        // The link comes from the authenticated subject/client, never a supplied user ID.
        return Ok(new { id = identity.Link.Id, appId = identity.App.Id, sub = identity.Link.Subject });
    }
    [HttpDelete("~/v1/account-links/{id:guid}")]
    public async Task<IActionResult> Unlink(Guid id, CancellationToken ct)
    {
        var identity = await Identity(ct); if (identity is null) return Unauthorized();
        if (!User.HasScope("openid") || identity.Link.Id != id) return StatusCode(403);
        await runtime.RevokeConsentAsync(identity.Account.Id, identity.App.Id, ct);
        return NoContent();
    }
}
