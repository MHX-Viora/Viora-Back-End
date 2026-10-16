using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

// Applications are a transactional library projection. Registry credentials remain authoritative.
public sealed class MiniAppOidcRegistry(AppDbContext db, MiniAppSecretProtector protector, IOpenIddictApplicationManager applications)
{
    public async Task SynchronizeAsync(Guid appId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("OIDC registry synchronization requires the registry transaction.");
        var app = await db.MiniApps.SingleAsync(x => x.Id == appId, ct);
        var clients = await db.MiniAppOAuthClients.Where(x => x.MiniAppId == appId).ToListAsync(ct);
        foreach (var client in clients)
        {
            var existing = await applications.FindByClientIdAsync(client.ClientKey, ct);
            if (app.DeletedAt != null || !app.SsoEnabled || client.RevokedAt != null)
            {
                if (existing != null) await applications.DeleteAsync(existing, ct);
                continue;
            }
            if (client.ClientType is not ("Public" or "Confidential"))
                throw new DeveloperAuthException(409, "SSO_CLIENT_TYPE_INVALID", "Loại OAuth Client không hợp lệ.");
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientKey,
                ClientType = client.ClientType == "Public" ? OpenIddictConstants.ClientTypes.Public : OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
                DisplayName = app.Name
            };
            if (client.ClientType == "Confidential")
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
            foreach (var scope in client.AllowedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SsoClientPolicy.SupportedScopes.Contains(scope, StringComparer.Ordinal))
                    throw new DeveloperAuthException(400, "SSO_INVALID_SCOPE", "Quyền truy cập không hợp lệ.");
                if (scope != "openid") descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
            }
            var redirects = await db.MiniAppRedirectUris.Where(x => x.MiniAppId == appId && x.Environment == client.Environment)
                .Select(x => x.RedirectUri).ToListAsync(ct);
            foreach (var redirect in redirects) descriptor.RedirectUris.Add(new Uri(redirect, UriKind.Absolute));
            if (existing == null) await applications.CreateAsync(descriptor, ct);
            else await applications.UpdateAsync(existing, descriptor, ct);
        }
    }
}
