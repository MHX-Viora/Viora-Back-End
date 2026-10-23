using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppProductionCleanupResult(int EventsDeleted, int GrantsExpired, int SessionsClosed, int GrantsDeleted, int SessionsDeleted);

public sealed class MiniAppProductionRetentionService(AppDbContext db, TimeProvider clock, MiniAppProductionOptions options, MiniAppProductionAudit audit)
{
    public async Task<MiniAppProductionCleanupResult> CleanupAsync(CancellationToken ct)
    {
        if (!options.Enabled && !options.CleanupEnabled) return new(0, 0, 0, 0, 0);
        if (options.EventRetentionDays is < 1 or > 30) throw new InvalidOperationException("Production retention is outside policy limits.");
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Retention owns its transaction.");
        var now = clock.GetUtcNow().UtcDateTime; var cutoff = now.AddDays(-options.EventRetentionDays);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var eventIds = await db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.CreatedAt < cutoff)
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(100).Select(x => x.Id).ToArrayAsync(ct);
            var eventsDeleted = await db.Set<MiniAppProductionRuntimeEvent>().Where(x => eventIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
            var expired = await db.Set<MiniAppProductionLaunchGrant>().Where(x => x.ExpiresAt <= now && x.ConsumedAt == null)
                .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(100).Select(x => x.Id).ToArrayAsync(ct);
            var version = Guid.NewGuid();
            var grantsExpired = await db.Set<MiniAppProductionLaunchGrant>().Where(x => expired.Contains(x.Id) && x.ConsumedAt == null && x.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now).SetProperty(x => x.ReceiptProtected, (string?)null).SetProperty(x => x.Version, version), ct);
            var ending = await db.Set<MiniAppProductionHostSession>().AsNoTracking().Where(x => x.ClosedAt == null && x.ExpiresAt >= cutoff &&
                (x.ExpiresAt <= now || x.RuntimeCredentialHash == null && !db.Set<MiniAppProductionLaunchGrant>().Any(g => g.HostSessionId == x.Id && g.ConsumedAt == null && g.ExpiresAt > now)))
                .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(100).ToArrayAsync(ct);
            var sessionsClosed = 0;
            foreach (var session in ending)
            {
                var changed = await db.Set<MiniAppProductionHostSession>().Where(x => x.Id == session.Id && x.Version == session.Version && x.ClosedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClosedAt, now).SetProperty(x => x.RuntimeCredentialHash, (string?)null).SetProperty(x => x.Version, version), ct);
                if (changed == 1) { sessionsClosed++; audit.Append(session, MiniAppProductionEventCode.Invalidated); }
            }
            await db.SaveChangesAsync(ct);
            // Old expired history is removed directly; creating a fresh audit for it
            // would unnecessarily retain the account/session reference another week.
            var obsolete = await db.Set<MiniAppProductionHostSession>().Where(x => x.ExpiresAt < cutoff &&
                !db.Set<MiniAppProductionRuntimeEvent>().Any(e => e.HostSessionId == x.Id))
                .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(100).Select(x => x.Id).ToArrayAsync(ct);
            var grantsDeleted = await db.Set<MiniAppProductionLaunchGrant>().Where(x => obsolete.Contains(x.HostSessionId) && x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
            var sessionsDeleted = await db.Set<MiniAppProductionHostSession>().Where(x => obsolete.Contains(x.Id) && x.ExpiresAt < cutoff &&
                !db.Set<MiniAppProductionRuntimeEvent>().Any(e => e.HostSessionId == x.Id) &&
                !db.Set<MiniAppProductionLaunchGrant>().Any(g => g.HostSessionId == x.Id)).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct); return new(eventsDeleted, grantsExpired, sessionsClosed, grantsDeleted, sessionsDeleted);
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error))
        { db.ChangeTracker.Clear(); return new(0, 0, 0, 0, 0); }
    }
}
