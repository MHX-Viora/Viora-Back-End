using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Viora.Infrastructure.Sso;
using Microsoft.AspNetCore;
using Viora.Infrastructure.Developers;
using viora_BE.Developer;

namespace viora_BE.Sso;

public sealed class SsoSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, SsoSessionService sessions, SandboxTestService tests) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AnktSsoSession";
    public const string CookieName = "__Host-AnktSsoSession";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetOpenIddictServerRequest()?.ClientId is { } clientId && Request.Cookies.TryGetValue(SandboxTestApi.ClientCookie(clientId), out var testCredential))
        {
            var test = await tests.AuthenticateAsync(testCredential, clientId, Context.RequestAborted);
            if (test == null) return AuthenticateResult.Fail("Sandbox test session is unavailable for this client.");
            var testIdentity = new ClaimsIdentity([new Claim("sub", test.Identity.AccountId.ToString()),
                new Claim(SandboxTestService.SessionClaim, test.Id.ToString())], SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(testIdentity), SchemeName));
        }
        if (!Request.Cookies.TryGetValue(CookieName, out var credential)) return AuthenticateResult.NoResult();
        var session = await sessions.AuthenticateAsync(credential, Context.RequestAborted);
        if (session == null) return AuthenticateResult.Fail("Invalid SSO session.");
        var identity = new ClaimsIdentity([
            new Claim("sub", session.AccountId.ToString()), new Claim("social_session", session.SocialSessionId.ToString()),
            new Claim("sso_session", session.Id.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
