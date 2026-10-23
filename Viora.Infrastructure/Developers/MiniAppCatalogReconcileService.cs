using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppCatalogReconcileResult(string AppId, string Status, bool Written);

// Operator-only helper; no API exposes this capability or creates review/publication authority.
public sealed class MiniAppCatalogReconcileService(AppDbContext db, TimeProvider clock)
{
    public async Task<MiniAppCatalogReconcileResult> ReconcileAsync(string appId, bool apply, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Reconciliation owns its transaction.");
        await using var tx = apply ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var id = await db.MiniApps.AsNoTracking().Where(x => x.AppId == appId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (id == null) return new(appId, "unavailable", false);
            var now = clock.GetUtcNow().UtcDateTime;
            var target = await MiniAppProductionEligibility.ReadAsync(db, id.Value, now, ct);
            var exists = await db.Set<MiniAppCatalogPublication>().AnyAsync(x => x.PublicationId == target.Publication.Id &&
                x.MiniAppVersionId == target.Version.Id && x.SnapshotHash == target.Version.SnapshotHash, ct);
            if (!exists && apply)
                await new MiniAppCatalogProjection(db, new() { Enabled = true }).AppendAsync(target.Publication, target.Version, now, ct);
            if (tx != null) await tx.CommitAsync(ct);
            return new(appId, exists ? "already-present" : apply ? "applied" : "ready", !exists && apply);
        }
        catch (DeveloperAuthException) { return new(appId, "unavailable", false); }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { return new(appId, "retry-required", false); }
    }
}
