using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SsoAuthorizationController(SsoClientReader clients, SsoConsentService consents, SsoConsentInteraction interaction, IAntiforgery antiforgery, SandboxProtocolObserver sandbox, Viora.Infrastructure.Developers.SandboxTestService? tests = null) : Controller
{
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing validated OIDC request.");
        Viora.Domain.Developers.MiniAppOAuthClient client;
        try { client = await clients.EligibleAsync(request.ClientId, request.GetScopes(), null, ct); }
        catch (DeveloperAuthException e) { return Error(e.Code == "SSO_INVALID_SCOPE" ? OpenIddictConstants.Errors.InvalidScope : OpenIddictConstants.Errors.AccessDenied); }
        if (string.IsNullOrEmpty(request.Nonce)) return Error(OpenIddictConstants.Errors.InvalidRequest);
        if (tests != null && client.Environment == Viora.Domain.Developers.MiniAppOAuthEnvironment.Sandbox &&
            (!tests.Enabled || !Request.Cookies.ContainsKey(viora_BE.Developer.SandboxTestApi.ClientCookie(request.ClientId!)))) return Error(OpenIddictConstants.Errors.LoginRequired);
        var user = await HttpContext.AuthenticateAsync(SsoSessionHandler.SchemeName);
        if (!user.Succeeded)
        {
            if (Request.Cookies.ContainsKey(viora_BE.Developer.SandboxTestApi.ClientCookie(request.ClientId!))) return Error(OpenIddictConstants.Errors.AccessDenied);
            if (request.GetPromptValues().Contains(OpenIddictConstants.PromptValues.None)) return Error(OpenIddictConstants.Errors.LoginRequired);
            return Redirect("/sso/start?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
        }
        HttpContext.User = user.Principal!;
        var accountId = Guid.Parse(user.Principal!.FindFirst("sub")!.Value);
        var grant = await consents.FindAsync(accountId, request.ClientId!, request.GetScopes(), ct);
        // The authenticated owner already requested this bound test launch. Only
        // the Sandbox cookie can authorize the shared account without a prompt.
        if (grant == null && tests is { Enabled: true } &&
            client.Environment == Viora.Domain.Developers.MiniAppOAuthEnvironment.Sandbox &&
            !request.GetPromptValues().Contains(OpenIddictConstants.PromptValues.Consent) &&
            Guid.TryParse(User.FindFirst(Viora.Infrastructure.Developers.SandboxTestService.SessionClaim)?.Value, out var testSession))
        {
            try
            {
                var bound = await tests.RequireAsync(testSession, request.ClientId, ct);
                if (bound.Identity.AccountId != accountId) return Error(OpenIddictConstants.Errors.AccessDenied);
                grant = await consents.GrantAsync(accountId, request.ClientId!, request.GetScopes(), ct);
            }
            catch (DeveloperAuthException) { return Error(OpenIddictConstants.Errors.AccessDenied); }
            catch (DbUpdateException) { return ConsentConflict(); }
            catch (Npgsql.PostgresException e) when (e.SqlState is "40001" or "40P01") { return ConsentConflict(); }
        }
        if (grant != null && !request.GetPromptValues().Contains(OpenIddictConstants.PromptValues.Consent))
            return SignIn(await SandboxPrincipal(accountId,request,ct), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (request.GetPromptValues().Contains(OpenIddictConstants.PromptValues.None)) return Error(OpenIddictConstants.Errors.ConsentRequired);
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var csrf = antiforgery.GetAndStoreTokens(HttpContext);
        var prompt = new { miniApp = client.MiniApp.Name, developer = client.MiniApp.Developer.FullName, logoUrl = client.MiniApp.LogoUrl,
            environment=client.Environment.ToString(),
            scopes = request.GetScopes(), parameters = request.GetParameters()
                .Where(x => x.Key is not ("decision" or "consent_ticket" or "__RequestVerificationToken"))
                .ToDictionary(x => x.Key, x => x.Value.ToString()),
            csrfToken = csrf.RequestToken, consentTicket = interaction.Issue(accountId, request) };
        if (Request.GetTypedHeaders().Accept?.Any(x => x.MediaType.Value == "application/json") == true) return Json(prompt);
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        // Chromium applies form-action to the subsequent OIDC redirect as well.
        // OpenIddict has already validated this exact registered redirect URI.
        var callbackOrigin = new Uri(request.RedirectUri!).GetLeftPart(UriPartial.Authority);
        Response.Headers["Content-Security-Policy"] = $"default-src 'none'; style-src 'nonce-{nonce}'; img-src https:; form-action 'self' {callbackOrigin}; base-uri 'none'; frame-ancestors 'none'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "DENY";
        var logo = Uri.TryCreate(prompt.logoUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
        return View("~/Views/Sso/Consent.cshtml", new SsoConsentPage(prompt.miniApp, prompt.developer, logo,
            prompt.scopes.ToArray(), prompt.parameters, prompt.csrfToken!, prompt.consentTicket, nonce,prompt.environment));
    }

    [HttpPost("~/connect/authorize")]
    [Authorize(AuthenticationSchemes = SsoSessionHandler.SchemeName)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Consent([FromForm] string decision, [FromForm(Name = "consent_ticket")] string? ticket, CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing validated OIDC request.");
        var accountId = Guid.Parse(User.FindFirst("sub")!.Value);
        if (Request.Form["decision"].Count != 1 || Request.Form["consent_ticket"].Count != 1) return Error(OpenIddictConstants.Errors.InvalidRequest);
        if (!interaction.Validate(ticket, accountId, request)) return Error(OpenIddictConstants.Errors.InvalidRequest);
        if (decision == "deny") return Error(OpenIddictConstants.Errors.AccessDenied);
        if (decision != "allow") return Error(OpenIddictConstants.Errors.InvalidRequest);
        try
        {
            await consents.GrantAsync(accountId, request.ClientId!, request.GetScopes(), ct);
            return SignIn(await SandboxPrincipal(accountId,request,ct), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        catch (DeveloperAuthException) { return Error(OpenIddictConstants.Errors.AccessDenied); }
        catch (DbUpdateException) { return ConsentConflict(); }
        catch (Npgsql.PostgresException e) when (e.SqlState is "40001" or "40P01") { return ConsentConflict(); }
    }

    private async Task<System.Security.Claims.ClaimsPrincipal> SandboxPrincipal(Guid accountId,OpenIddictRequest request,CancellationToken ct)
    {
        var principal = await consents.PrincipalAsync(accountId,request.ClientId!,request.GetScopes(),ct);
        if (User.FindFirst(Viora.Infrastructure.Developers.SandboxTestService.SessionClaim)?.Value is { } session)
        {
            principal.SetClaim(Viora.Infrastructure.Developers.SandboxTestService.SessionClaim, session);
            SsoProfileClaims.Destinations(principal);
        }
        return await sandbox.BindAsync(principal, (string?)request.GetParameter("sandbox_run"),request.ClientId!,ct);
    }

    private ConflictObjectResult ConsentConflict() => Conflict(new { error = new { code = "SSO_CONSENT_CONFLICT", message = "Quyền vừa thay đổi. Vui lòng tải lại và thử lại." } });

    private ForbidResult Error(string error) => Forbid(new AuthenticationProperties(new Dictionary<string,string?>
    { [OpenIddictServerAspNetCoreConstants.Properties.Error] = error }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
