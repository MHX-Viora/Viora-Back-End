using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Developers;

namespace Viora.Infrastructure.Sso;

// Protocol authentication is performed by OpenIddict before this fresh registry/grant check.
public sealed class SsoTokenService(AppDbContext db, SsoClientReader clients, SsoConsentService consents, SandboxTestService? sandboxTest = null)
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
        if (await db.Accounts.IgnoreQueryFilters().AnyAsync(x => x.Id == consent.AccountId && x.IsSandbox, ct))
        {
            if (sandboxTest == null || !Guid.TryParse(principal.GetClaim(SandboxTestService.SessionClaim), out var sessionId)) throw Denied();
            var session = await sandboxTest.RequireAsync(sessionId, clientId, ct);
            if (session.Identity.AccountId != consent.AccountId) throw Denied();
        }
        if (grant == null || grant.AuthorizationId != authorizationId || grant.Subject != principal.GetClaim(OpenIddictConstants.Claims.Subject)) throw Denied();
        var fresh = await consents.PrincipalAsync(consent.AccountId, clientId, scopes, ct);
        foreach (var type in SsoProfileClaims.Public.Where(x => x is not ("sub" or "email_verified" or "isSandbox"))) principal.SetClaim(type, fresh.GetClaim(type));
        principal.SetClaim("isSandbox", fresh.GetClaim("isSandbox") is { } test ? bool.Parse(test) : (bool?)null);
        principal.SetClaim("email_verified", fresh.GetClaim("email_verified") is { } verified ? bool.Parse(verified) : (bool?)null);
        SsoProfileClaims.Destinations(principal);
        return principal;
    }
    private static DeveloperAuthException Denied() => new(403, "SSO_GRANT_UNAVAILABLE", "Quyền truy cập không còn hiệu lực.");
}
