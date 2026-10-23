using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed class MiniAppPublicationRevocation(AppDbContext db, IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens, TimeProvider clock)
{
    public Task RevokeProductionAsync(Guid appId, CancellationToken ct) => RevokeAsync(appId, MiniAppOAuthEnvironment.Production, ct);
    public Task RevokeAllAsync(Guid appId, CancellationToken ct) => RevokeAsync(appId, null, ct);
    private async Task RevokeAsync(Guid appId, MiniAppOAuthEnvironment? environment, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Publication revocation requires the command transaction.");
        var keys = await db.MiniAppOAuthClients.AsNoTracking().Where(x => x.MiniAppId == appId && (environment == null || x.Environment == environment))
            .Select(x => x.ClientKey).ToArrayAsync(ct);
        foreach (var key in keys)
        {
            var application = await applications.FindByClientIdAsync(key, ct);
            if (application == null) continue;
            var id = await applications.GetIdAsync(application, ct) ?? throw new DbUpdateConcurrencyException("Projection identity changed.");
            await tokens.RevokeByApplicationIdAsync(id, ct);
            await authorizations.RevokeByApplicationIdAsync(id, ct);
        }
        var now = clock.GetUtcNow().UtcDateTime; var revision = Guid.NewGuid();
        await db.MiniAppUserConsents.Where(x => x.MiniAppId == appId && (environment == null || x.Environment == environment) && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, revision), ct);
    }
}
