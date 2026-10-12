using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Persistence;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Viora.Infrastructure.OpenApi;

public sealed class MiniAppOAuthApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication> cache,
    ILogger<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication> store,
    AppDbContext db, OpenApiDbContext protocol, IClientCredentialService credentials)
    : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>(cache, logger, options, store)
{
    public override async ValueTask<OpenIddictEntityFrameworkCoreApplication?> FindByClientIdAsync(string identifier, CancellationToken ct = default)
    {
        var app = await db.MiniApps.AsNoTracking().Include(a => a.Developer).Include(a => a.PermissionMappings).ThenInclude(m => m.Permission)
            .SingleOrDefaultAsync(a => a.ClientId == identifier && a.DeletedAt == null && a.Status == MiniAppStatus.Active && a.Developer.Status == DeveloperStatus.Active, ct);
        if (app is null) return null;
        var published = MiniAppConfigurationSnapshot.Published(app);
        if (published.AuthenticationMode != "AnktSso") return null;
        var entry = await protocol.Set<OpenIddictEntityFrameworkCoreApplication>().SingleOrDefaultAsync(a => a.ClientId == identifier, ct);
        var inserting = entry is null;
        if (entry is null) { entry = new() { Id = app.Id.ToString(), ClientId = identifier }; protocol.Add(entry); }
        entry.DisplayName = published.Name;
        entry.ClientType = published.ClientAuthenticationMethod == "None" ? ClientTypes.Public : ClientTypes.Confidential;
        // Reuse the existing one-way secret hash; custom validation below also honors rotation.
        entry.ClientSecret = entry.ClientType == ClientTypes.Confidential ? app.ClientSecretHash : null;
        entry.ConsentType = ConsentTypes.Explicit;
        entry.RedirectUris = JsonSerializer.Serialize(published.CallbackUrls);
        entry.Requirements = JsonSerializer.Serialize(new[] { Requirements.Features.ProofKeyForCodeExchange });
        entry.Permissions = JsonSerializer.Serialize(new[] { Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation, Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code }
            .Concat(OpenApiPolicy.ScopePermissions.Where(s => published.Permissions.Contains(s.Value)).Select(s => Permissions.Prefixes.Scope + s.Key)));
        try { await protocol.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (inserting)
        {
            protocol.Entry(entry).State = EntityState.Detached;
            if (!await protocol.Set<OpenIddictEntityFrameworkCoreApplication>().AnyAsync(a => a.ClientId == identifier, ct)) throw;
            return await FindByClientIdAsync(identifier, ct);
        }
        return entry;
    }
    public override async ValueTask<bool> ValidateClientSecretAsync(OpenIddictEntityFrameworkCoreApplication application, string secret, CancellationToken ct = default)
    {
        var hash = await db.MiniApps.Where(a => a.ClientId == application.ClientId && a.DeletedAt == null && a.Status == MiniAppStatus.Active && a.Developer.Status == DeveloperStatus.Active).Select(a => a.ClientSecretHash).SingleOrDefaultAsync(ct);
        return hash is not null && credentials.VerifySecret(secret, hash);
    }
}
