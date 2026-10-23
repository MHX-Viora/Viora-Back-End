using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public static class MiniAppPublicationPolicy
{
    public static async Task<MiniAppVersionSnapshot> ReadAsync(AppDbContext db, MiniAppOAuthClient client, DateTime now, CancellationToken ct)
    {
        if (client.Environment != MiniAppOAuthEnvironment.Production || client.RevokedAt != null) throw Unavailable();
        MiniAppProductionTarget target;
        try { target = await MiniAppProductionEligibility.PublicationAsync(db, client.MiniAppId, now, ct); }
        catch (DeveloperAuthException) { throw Unavailable(); }
        var snapshot = target.Snapshot;
        if (target.Publication.ProductionOAuthClientId != client.Id || !await db.MiniAppOAuthClients.AsNoTracking()
            .AnyAsync(x => x.Id == client.Id && x.MiniAppId == client.MiniAppId && x.Environment == MiniAppOAuthEnvironment.Production &&
                x.RevokedAt == null && x.ClientKey == client.ClientKey, ct)) throw Unavailable();
        if (!snapshot.SsoEnabled || snapshot.Production is not { } intent || intent.ClientIdentity != client.Id
            || intent.ClientId != client.ClientKey) throw Unavailable();
        return snapshot;
    }

    private static DeveloperAuthException Unavailable() => new(403, "SSO_CLIENT_UNAVAILABLE", "Ứng dụng không khả dụng.");
}
