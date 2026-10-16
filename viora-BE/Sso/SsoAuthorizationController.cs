using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

public sealed class SsoAuthorizationController(SsoClientReader clients) : Controller
{
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing validated OIDC request.");
        try { await clients.EligibleAsync(request.ClientId, request.GetScopes(), null, ct); }
        catch (DeveloperAuthException e) { return Error(e.Code == "SSO_INVALID_SCOPE" ? OpenIddictConstants.Errors.InvalidScope : OpenIddictConstants.Errors.AccessDenied); }
        if (string.IsNullOrEmpty(request.Nonce)) return Error(OpenIddictConstants.Errors.InvalidRequest);
        var user = await HttpContext.AuthenticateAsync(SsoSessionHandler.SchemeName);
        if (!user.Succeeded)
        {
            if (request.GetPromptValues().Contains(OpenIddictConstants.PromptValues.None)) return Error(OpenIddictConstants.Errors.LoginRequired);
            return Redirect("/sso/start?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
        }
        // Consent persistence/issuance follows in Tasks 10–12. Until then deny securely.
        return Error(OpenIddictConstants.Errors.ConsentRequired);
    }

    private ForbidResult Error(string error) => Forbid(new AuthenticationProperties(new Dictionary<string,string?>
    { [OpenIddictServerAspNetCoreConstants.Properties.Error] = error }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
