using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

// Protocol authentication is performed by OpenIddict before this fresh registry/grant check.
public sealed class SsoTokenService(AppDbContext db, SsoClientReader clients, SsoConsentService consents)
{
    public async Task<ClaimsPrincipal> RefreshClaimsAsync(ClaimsPrincipal principal, string? expectedClient, CancellationToken ct, string? browserOrigin = null)
    {
        var presenters = principal.GetPresenters();
        if (presenters.Length != 1 || (expectedClient != null && presenters[0] != expectedClient)) throw Denied();
        var clientId = presenters[0]; var scopes = principal.GetScopes();
        var client = await clients.EligibleAsync(clientId, scopes, null, ct);
        await clients.EnsureBrowserOriginAsync(client, browserOrigin, ct);
        // Codes retain private resource metadata; access tokens carry the signed audience.
        var resources = expectedClient == null ? principal.GetAudiences() : principal.GetResources();
        if (!resources.SequenceEqual([SsoClientPolicy.Resource(client.Environment)])) throw Denied();
        var authorizationId = principal.GetAuthorizationId();
        if (string.IsNullOrEmpty(authorizationId)) throw Denied();
        var consent = await db.MiniAppUserConsents.AsNoTracking().SingleOrDefaultAsync(x => x.AuthorizationId == authorizationId &&
            x.OAuthClientId == client.Id && x.MiniAppId == client.MiniAppId && x.Environment == client.Environment && x.RevokedAt == null, ct) ?? throw Denied();
        var grant = await consents.FindAsync(consent.AccountId, clientId, scopes, ct);
        if (grant == null || grant.AuthorizationId != authorizationId || grant.Subject != principal.GetClaim(OpenIddictConstants.Claims.Subject)) throw Denied();
        var fresh = await consents.PrincipalAsync(consent.AccountId, clientId, scopes, ct);
        principal.SetClaim(OpenIddictConstants.Claims.Name, fresh.GetClaim(OpenIddictConstants.Claims.Name));
        principal.SetClaim(OpenIddictConstants.Claims.Picture, fresh.GetClaim(OpenIddictConstants.Claims.Picture));
        principal.SetClaim(OpenIddictConstants.Claims.Email, fresh.GetClaim(OpenIddictConstants.Claims.Email));
        principal.SetClaim(OpenIddictConstants.Claims.EmailVerified, fresh.HasClaim(OpenIddictConstants.Claims.EmailVerified, "true") ? true : (bool?)null);
        principal.SetDestinations(claim => claim.Type is OpenIddictConstants.Claims.Subject or OpenIddictConstants.Claims.Name or
            OpenIddictConstants.Claims.Picture or OpenIddictConstants.Claims.Email or OpenIddictConstants.Claims.EmailVerified
            ? new[] { OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken } : Array.Empty<string>());
        return principal;
    }
    private static DeveloperAuthException Denied() => new(403, "SSO_GRANT_UNAVAILABLE", "Quyền truy cập không còn hiệu lực.");
}
