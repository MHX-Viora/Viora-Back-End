using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppBridgeRevocation(AppDbContext db, TimeProvider clock)
{
    public Task RevokeProductionAsync(Guid appId, CancellationToken ct) => RevokeAsync(appId, false, ct);
    public Task RevokeAllAsync(Guid appId, CancellationToken ct) => RevokeAsync(appId, null, ct);
    private Task<int> RevokeAsync(Guid appId, bool? sandbox, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Bridge revocation requires the command transaction.");
        var now = clock.GetUtcNow().UtcDateTime;
        return db.Set<Viora.Domain.Developers.MiniAppBridgeSession>()
            .Where(x => x.MiniAppId == appId && x.RevokedAt == null && (sandbox == null || x.IsSandbox == sandbox))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now), ct);
    }
}
