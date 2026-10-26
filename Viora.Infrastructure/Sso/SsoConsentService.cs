using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Developers;

namespace Viora.Infrastructure.Sso;

public sealed record SsoGrant(string Subject, string AuthorizationId, MiniAppOAuthEnvironment Environment);

public sealed class SsoConsentService(AppDbContext db, SsoClientReader clients, IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens, TimeProvider clock, SandboxTestService? sandboxTest = null)
{
    public async Task<SsoGrant?> FindAsync(Guid accountId, string clientId, IEnumerable<string> requested, CancellationToken ct)
    {
        var scopes = requested.ToArray(); var client = await clients.EligibleAsync(clientId, scopes, null, ct); var now = clock.GetUtcNow().UtcDateTime;
        var consent = await db.MiniAppUserConsents.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId && x.OAuthClientId == client.Id &&
            x.MiniAppId == client.MiniAppId && x.Environment == client.Environment && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), ct);
        if (consent?.AuthorizationId == null || scopes.Except(consent.GrantedScopes.Split(' '), StringComparer.Ordinal).Any()) return null;
        var authorization = await authorizations.FindByIdAsync(consent.AuthorizationId, ct);
        var application = await applications.FindByClientIdAsync(clientId, ct);
        var subject = await db.MiniAppPairwiseSubjects.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId && x.MiniAppId == client.MiniAppId && x.Environment == client.Environment, ct);
        if (authorization == null || application == null || subject == null || await authorizations.GetStatusAsync(authorization, ct) != OpenIddictConstants.Statuses.Valid ||
            await authorizations.GetSubjectAsync(authorization, ct) != subject.Subject ||
            await authorizations.GetApplicationIdAsync(authorization, ct) != await applications.GetIdAsync(application, ct) ||
            scopes.Except(await authorizations.GetScopesAsync(authorization, ct), StringComparer.Ordinal).Any()) return null;
        return new(subject.Subject, consent.AuthorizationId, client.Environment);
    }

    public async Task<SsoGrant> GrantAsync(Guid accountId, string clientId, IEnumerable<string> requested, CancellationToken ct)
    {
        var scopes = requested.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var client = await clients.EligibleAsync(clientId, scopes, null, ct);
        var account = await db.Accounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == accountId && x.Status == AccountStatus.Active && x.DeletedAt == null, ct) ?? throw Denied();
        if (sandboxTest != null && client.Environment == MiniAppOAuthEnvironment.Sandbox && (!sandboxTest.Enabled || !account.IsSandbox)) throw Denied();
        if (account.IsSandbox)
        {
            if (client.Environment != MiniAppOAuthEnvironment.Sandbox || sandboxTest == null) throw Denied();
            await sandboxTest.ProfileForAccountAsync(accountId, client.MiniAppId, ct);
        }
        var subject = await db.MiniAppPairwiseSubjects.SingleOrDefaultAsync(x => x.AccountId == accountId && x.MiniAppId == client.MiniAppId && x.Environment == client.Environment, ct);
        if (subject == null)
        {
            subject = new MiniAppPairwiseSubject { AccountId = accountId, MiniAppId = client.MiniAppId, Environment = client.Environment, Subject = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)) };
            db.MiniAppPairwiseSubjects.Add(subject); await db.SaveChangesAsync(ct);
        }
        var application = await applications.FindByClientIdAsync(clientId, ct) ?? throw Denied();
        var descriptor = new OpenIddictAuthorizationDescriptor { ApplicationId = await applications.GetIdAsync(application, ct), Subject = subject.Subject,
            Status = OpenIddictConstants.Statuses.Valid, Type = OpenIddictConstants.AuthorizationTypes.Permanent, CreationDate = clock.GetUtcNow() };
        descriptor.Scopes.UnionWith(scopes);
        var authorization = await authorizations.CreateAsync(descriptor, ct);
        var id = await authorizations.GetIdAsync(authorization, ct) ?? throw Denied();
        var consent = await db.MiniAppUserConsents.SingleOrDefaultAsync(x => x.AccountId == accountId && x.OAuthClientId == client.Id && x.RevokedAt == null, ct);
        if (consent != null)
        {
            if (consent.AuthorizationId != null)
            {
                var previous = await authorizations.FindByIdAsync(consent.AuthorizationId, ct);
                if (previous != null && !await authorizations.TryRevokeAsync(previous, ct)) throw Denied();
                await tokens.RevokeByAuthorizationIdAsync(consent.AuthorizationId, ct);
            }
            // Every new explicit approval retains the previous scope decision in owner history.
            consent.RevokedAt = clock.GetUtcNow().UtcDateTime; consent.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            consent = null;
        }
        if (consent == null)
        {
            consent = new MiniAppUserConsent { AccountId = accountId, MiniAppId = client.MiniAppId, OAuthClientId = client.Id, Environment = client.Environment };
            db.MiniAppUserConsents.Add(consent);
        }
        consent.AuthorizationId = id; consent.GrantedScopes = string.Join(' ', scopes); consent.GrantedAt = clock.GetUtcNow().UtcDateTime;
        consent.UpdatedAt = consent.GrantedAt; consent.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(subject.Subject, id, client.Environment);
    }

    public async Task<ClaimsPrincipal> PrincipalAsync(Guid accountId, string clientId, IEnumerable<string> requested, CancellationToken ct)
    {
        var scopes = requested.ToArray(); var grant = await FindAsync(accountId, clientId, scopes, ct) ?? throw Denied();
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.Id == accountId && x.Status == AccountStatus.Active && x.DeletedAt == null, ct) ?? throw Denied();
        if (sandboxTest != null && grant.Environment == MiniAppOAuthEnvironment.Sandbox && (!sandboxTest.Enabled || !account.IsSandbox)) throw Denied();
        SandboxTestProfile? testProfile = null;
        if (account.IsSandbox)
        {
            if (grant.Environment != MiniAppOAuthEnvironment.Sandbox || sandboxTest == null) throw Denied();
            var miniAppId = await db.MiniAppOAuthClients.Where(x => x.ClientKey == clientId).Select(x => x.MiniAppId).SingleAsync(ct);
            testProfile = await sandboxTest.ProfileForAccountAsync(accountId, miniAppId, ct);
        }
        var identity = new ClaimsIdentity("ANKT.OIDC", OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, grant.Subject));
        if (scopes.Contains("profile", StringComparer.Ordinal) && testProfile != null)
        {
            identity.AddClaim(new Claim("isSandbox", "true", ClaimValueTypes.Boolean));
            foreach (var (type, value) in new[] { ("name", testProfile.FullName), ("preferred_username", testProfile.Username),
                ("id", testProfile.Id.ToString()), ("username", testProfile.Username), ("displayName", testProfile.DisplayName),
                ("fullName", testProfile.FullName), ("avatarUrl", testProfile.AvatarUrl), ("coverUrl", testProfile.CoverUrl),
                ("nickname", testProfile.Nickname), ("gender", testProfile.Gender.ToLowerInvariant()), ("birthdate", testProfile.Birthdate),
                ("picture", testProfile.AvatarUrl), ("ankt_cover_picture", testProfile.CoverUrl) })
                if (!string.IsNullOrEmpty(value)) identity.AddClaim(new Claim(type, value));
        }
        else if (scopes.Contains("profile", StringComparer.Ordinal) && account.User != null)
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, account.User.DisplayName));
            if (Uri.TryCreate(account.User.AvatarUrl, UriKind.Absolute, out var picture) && picture.Scheme == "https" && picture.UserInfo.Length == 0)
                identity.AddClaim(new Claim(OpenIddictConstants.Claims.Picture, picture.AbsoluteUri));
        }
        if (scopes.Contains("email", StringComparer.Ordinal) && testProfile != null)
        {
            identity.AddClaim(new Claim("email", testProfile.Email));
            identity.AddClaim(new Claim("email_verified", "false", ClaimValueTypes.Boolean));
        }
        else if (scopes.Contains("email", StringComparer.Ordinal) && account.EmailVerifiedAt != null && !string.IsNullOrWhiteSpace(account.Email))
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, account.Email));
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.EmailVerified, "true", ClaimValueTypes.Boolean));
        }
        var principal = new ClaimsPrincipal(identity); principal.SetScopes(scopes); principal.SetResources(SsoClientPolicy.Resource(grant.Environment));
        principal.SetAuthorizationId(grant.AuthorizationId);
        SsoProfileClaims.Destinations(principal);
        return principal;
    }

    private static DeveloperAuthException Denied() => new(403, "SSO_AUTHORIZATION_REQUIRED", "Chưa được cấp quyền đăng nhập ứng dụng.");
}
