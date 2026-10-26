using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableCors(SsoCorsPolicyProvider.PolicyName)]
public sealed class SsoTokenController(SsoTokenService tokens, SandboxProtocolObserver sandbox) : ControllerBase
{
    [HttpPost("~/connect/token")]
    public async Task<IActionResult> Exchange(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing validated OIDC request.");
        if (!request.IsAuthorizationCodeGrantType()) return Error(OpenIddictConstants.Errors.UnsupportedGrantType);
        var authentication = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!authentication.Succeeded) return Error(OpenIddictConstants.Errors.InvalidGrant);
        try
        {
            if (Request.Headers.Origin.Count > 1) return Error(OpenIddictConstants.Errors.InvalidRequest);
            var principal = await tokens.RefreshClaimsAsync(authentication.Principal!, request.ClientId, ct,
                Request.Headers.ContainsKey("Origin") ? Request.Headers.Origin.ToString() : null);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        catch (DeveloperAuthException) { return Error(OpenIddictConstants.Errors.InvalidGrant); }
    }

    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo(CancellationToken ct)
    {
        var authentication = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!authentication.Succeeded) return Error(OpenIddictConstants.Errors.InvalidToken);
        try
        {
            if (Request.Headers.Origin.Count > 1) return Error(OpenIddictConstants.Errors.InvalidRequest);
            var principal = await tokens.RefreshClaimsAsync(authentication.Principal!, null, ct,
                Request.Headers.ContainsKey("Origin") ? Request.Headers.Origin.ToString() : null);
            var result = new Dictionary<string, object> { ["sub"] = principal.GetClaim(OpenIddictConstants.Claims.Subject)! };
            foreach (var claim in SsoProfileClaims.Public.Where(x => x is not ("sub" or "email_verified" or "isSandbox")))
                if (principal.GetClaim(claim) is { } value) result[claim] = value;
            if (principal.GetClaim("isSandbox") is { } isSandbox) result["isSandbox"] = bool.TryParse(isSandbox, out var test) && test;
            if (principal.GetClaim("email_verified") is { } verified) result["email_verified"] = bool.TryParse(verified, out var emailVerified) && emailVerified;
            await sandbox.UserInfoAsync(principal,ct);
            return Ok(result);
        }
        catch (DeveloperAuthException) { return Error(OpenIddictConstants.Errors.InvalidToken); }
    }

    private ForbidResult Error(string error) => Forbid(new AuthenticationProperties(new Dictionary<string,string?>
    { [OpenIddictServerAspNetCoreConstants.Properties.Error] = error }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
