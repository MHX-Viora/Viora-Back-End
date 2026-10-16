using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

public sealed class SsoSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, SsoSessionService sessions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AnktSsoSession";
    public const string CookieName = "__Host-AnktSsoSession";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var credential)) return AuthenticateResult.NoResult();
        var session = await sessions.AuthenticateAsync(credential, Context.RequestAborted);
        if (session == null) return AuthenticateResult.Fail("Invalid SSO session.");
        var identity = new ClaimsIdentity([
            new Claim("sub", session.AccountId.ToString()), new Claim("social_session", session.SocialSessionId.ToString()),
            new Claim("sso_session", session.Id.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
