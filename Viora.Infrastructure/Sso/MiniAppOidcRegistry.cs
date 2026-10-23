using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

// Applications are a transactional library projection. Registry credentials remain authoritative.
public sealed class MiniAppOidcRegistry(AppDbContext db, MiniAppSecretProtector protector, IOpenIddictApplicationManager applications)
{
    public async Task SynchronizePublicationAsync(Guid appId, Guid? requiredClientId, CancellationToken ct)
    {
        await SynchronizeAsync(appId, ct);
        await VerifyPublicationAsync(appId, requiredClientId, ct);
    }

    public async Task VerifyPublicationAsync(Guid appId, Guid? requiredClientId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Publication verification requires the command transaction.");
        var clients = await db.MiniAppOAuthClients.AsNoTracking().Where(x => x.MiniAppId == appId && x.Environment == MiniAppOAuthEnvironment.Production).ToArrayAsync(ct);
        if (requiredClientId != null && !clients.Any(x => x.Id == requiredClientId)) throw ProjectionUnavailable();
        foreach (var client in clients)
        {
            var projection = await applications.FindByClientIdAsync(client.ClientKey, ct);
            if (client.Id != requiredClientId) { if (projection != null) throw ProjectionUnavailable(); continue; }
            var snapshot = await MiniAppPublicationPolicy.ReadAsync(db, client, DateTime.UtcNow, ct); var intent = snapshot.Production!;
            var permissions = new HashSet<string>(StringComparer.Ordinal) { OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token, OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode, OpenIddictConstants.Permissions.ResponseTypes.Code };
            if (intent.ClientType == "Confidential") permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
            foreach (var scope in intent.Scopes.Where(x => x != "openid")) permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
            if (projection == null || await applications.GetClientTypeAsync(projection, ct) != intent.ClientType.ToLowerInvariant() ||
                await applications.GetConsentTypeAsync(projection, ct) != OpenIddictConstants.ConsentTypes.Explicit ||
                await applications.GetDisplayNameAsync(projection, ct) != snapshot.Name ||
                !permissions.SetEquals(await applications.GetPermissionsAsync(projection, ct)) ||
                !(await applications.GetRedirectUrisAsync(projection, ct)).Order(StringComparer.Ordinal).SequenceEqual(intent.RedirectUris.Order(StringComparer.Ordinal)) ||
                !(await applications.GetRequirementsAsync(projection, ct)).ToHashSet(StringComparer.Ordinal).SetEquals([OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange]))
                throw ProjectionUnavailable();
            var properties = await applications.GetPropertiesAsync(projection, ct);
            if (!properties.TryGetValue("registry_client_id", out var registryId) || registryId.GetString() != client.Id.ToString() ||
                !properties.TryGetValue("environment", out var environment) || environment.GetString() != "Production") throw ProjectionUnavailable();
            if (intent.ClientType == "Confidential")
            {
                var secret = await db.MiniAppClientSecrets.AsNoTracking().SingleOrDefaultAsync(x => x.OAuthClientId == client.Id && x.RevokedAt == null, ct);
                if (secret == null || secret.ExpiresAt <= DateTime.UtcNow || !await applications.ValidateClientSecretAsync(projection,
                    protector.Decrypt(secret.SecretEncrypted, secret.SecretKeyVersion, client.Id), ct)) throw ProjectionUnavailable();
            }
            else
            {
                var descriptor = new OpenIddictApplicationDescriptor(); await applications.PopulateAsync(descriptor, projection, ct);
                if (descriptor.ClientSecret != null) throw ProjectionUnavailable();
            }
        }
    }
    private static DeveloperAuthException ProjectionUnavailable() => new(409, "PUBLICATION_PROJECTION_UNAVAILABLE", "Cấu hình đăng nhập Production chưa được áp dụng đầy đủ.");

    public async Task SynchronizeAsync(Guid appId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("OIDC registry synchronization requires the registry transaction.");
        var app = await db.MiniApps.SingleAsync(x => x.Id == appId, ct);
        var suspended = app.Status == MiniAppStatus.Suspended || await db.MiniAppOperationalStates.AsNoTracking().AnyAsync(x => x.MiniAppId == appId && x.Status == MiniAppOperationalStatus.Suspended, ct);
        var clients = await db.MiniAppOAuthClients.Where(x => x.MiniAppId == appId).ToListAsync(ct);
        foreach (var client in clients)
        {
            var existing = await applications.FindByClientIdAsync(client.ClientKey, ct);
            MiniAppVersionSnapshot? published = null;
            if (client.Environment == MiniAppOAuthEnvironment.Production)
            {
                try { published = await MiniAppPublicationPolicy.ReadAsync(db, client, DateTime.UtcNow, ct); }
                catch (DeveloperAuthException)
                {
                    if (existing != null) await applications.DeleteAsync(existing, ct);
                    continue;
                }
            }
            if (suspended || app.DeletedAt != null || !(published?.SsoEnabled ?? app.SsoEnabled) || client.RevokedAt != null)
            {
                if (existing != null) await applications.DeleteAsync(existing, ct);
                continue;
            }
            var clientType = published?.Production?.ClientType ?? client.ClientType;
            var scopes = published?.Production?.Scopes ?? client.AllowedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (clientType is not ("Public" or "Confidential"))
                throw new DeveloperAuthException(409, "SSO_CLIENT_TYPE_INVALID", "Loại OAuth Client không hợp lệ.");
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientKey,
                ClientType = clientType == "Public" ? OpenIddictConstants.ClientTypes.Public : OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
                DisplayName = published?.Name ?? app.Name
            };
            if (clientType == "Confidential")
            {
                var secret = await db.MiniAppClientSecrets.SingleOrDefaultAsync(x => x.OAuthClientId == client.Id && x.RevokedAt == null, ct);
                if (secret == null || secret.ExpiresAt <= DateTime.UtcNow)
                {
                    // An unavailable environment must fail closed without blocking recovery in another.
                    if (existing != null) await applications.DeleteAsync(existing, ct);
                    continue;
                }
                descriptor.ClientSecret = protector.Decrypt(secret.SecretEncrypted, secret.SecretKeyVersion, client.Id);
            }
            descriptor.Properties["registry_client_id"] = JsonSerializer.SerializeToElement(client.Id.ToString());
            descriptor.Properties["environment"] = JsonSerializer.SerializeToElement(client.Environment.ToString());
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
            descriptor.Permissions.UnionWith([
                OpenIddictConstants.Permissions.Endpoints.Authorization, OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation, OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code
            ]);
            // Server policy additionally requires explicit operator opt-in at request time.
            if (clientType == "Confidential")
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
            foreach (var scope in scopes)
            {
                if (!SsoClientPolicy.SupportedScopes.Contains(scope, StringComparer.Ordinal))
                    throw new DeveloperAuthException(400, "SSO_INVALID_SCOPE", "Quyền truy cập không hợp lệ.");
                if (scope != "openid") descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
            }
            IEnumerable<string> redirects = published?.Production?.RedirectUris ?? (IEnumerable<string>)await db.MiniAppRedirectUris
                .Where(x => x.MiniAppId == appId && x.Environment == client.Environment).Select(x => x.RedirectUri).ToListAsync(ct);
            foreach (var redirect in redirects) descriptor.RedirectUris.Add(new Uri(redirect, UriKind.Absolute));
            if (existing == null) await applications.CreateAsync(descriptor, ct);
            else await applications.UpdateAsync(existing, descriptor, ct);
        }
    }
}
