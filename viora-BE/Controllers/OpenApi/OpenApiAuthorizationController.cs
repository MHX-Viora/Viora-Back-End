using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Viora.Infrastructure.OpenApi;
using viora_BE.OpenApi;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace viora_BE.Controllers.OpenApi;

[EnableRateLimiting("open-api")]
public sealed class OpenApiAuthorizationController(OpenApiAccessService access, PartnerOpenApiOptions settings, OpenApiPrincipalLimiter limiter) : Controller
{
    [HttpGet("~/oauth/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest()!;
        if (!string.IsNullOrWhiteSpace(request.Prompt) && !request.HasPromptValue(PromptValues.None) || request.GetParameters().ContainsKey("max_age"))
            return Reject(Errors.InvalidRequest, "Forced reauthentication and max_age are not supported by this release.");
        var ticket = (string?)request["login_ticket"];
        if (string.IsNullOrWhiteSpace(ticket))
        {
            if (request.HasPromptValue(PromptValues.None)) return Reject(Errors.LoginRequired, "An ANKT session handoff is required.");
            var parameters = Request.Query.ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
            parameters.Remove("login_ticket");
            return Redirect(QueryHelpers.AddQueryString(settings.AuthorizationUi, parameters));
        }
        var principal = await access.Redeem(ticket, request, ct);
        if (principal is null) return Reject(Errors.AccessDenied, "Authorization session expired or consent is no longer valid.");
        HttpContext.User = principal;
        using var lease = await limiter.Limiter.AcquireAsync(HttpContext, 1, ct);
        if (!lease.IsAcquired) return StatusCode(429, new { error = Errors.TemporarilyUnavailable });
        principal.SetResources(settings.Audience).SetAudiences(settings.Audience);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/oauth/token")]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest()!;
        if (!request.IsAuthorizationCodeGrantType()) return Reject(Errors.UnsupportedGrantType, "Only authorization_code is supported.");
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is null || await access.Validate(result.Principal, ct, requireActiveRuntime: true) is null) return Reject(Errors.InvalidGrant, "Authorization is no longer valid.");
        HttpContext.User = result.Principal;
        using var lease = await limiter.Limiter.AcquireAsync(HttpContext, 1, ct);
        if (!lease.IsAcquired) return StatusCode(429, new { error = Errors.TemporarilyUnavailable });
        result.Principal.SetResources(settings.Audience).SetAudiences(settings.Audience);
        return SignIn(result.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    private IActionResult Reject(string error, string description) => Forbid(new AuthenticationProperties(new Dictionary<string, string?> { [OpenIddictServerAspNetCoreConstants.Properties.Error] = error, [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
