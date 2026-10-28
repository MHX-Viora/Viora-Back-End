using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppProductionRuntimeService(AppDbContext db, TimeProvider clock, MiniAppProductionOptions options,
    MiniAppProductionPolicy policy, MiniAppProductionAccess access, MiniAppProductionBindingService bindings, MiniAppProductionAudit audit)
{
    private enum Action { Context, Heartbeat, Close, Event, Document }
    private sealed record RuntimeResult(MiniAppProductionPolicyTarget Target, DateTime ExpiresAt);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<MiniAppProductionContextDto> ContextAsync(Guid id, string? credential, string? nonce, string? browser, CancellationToken ct)
    {
        var result = await RunAsync(Action.Context, id, credential, nonce, browser, ct) ?? throw MiniAppProductionPolicy.Ended();
        return new(result.Target.AppId);
    }
    // Read-only document metadata is authenticated by both HttpOnly cookies.
    // Bridge/actions still require the nonce from the private in-memory parent.
    public async Task<MiniAppProductionDocumentDto> DocumentAsync(Guid id, string? credential, string? browser, CancellationToken ct)
    {
        var result = await RunAsync(Action.Document, id, credential, null, browser, ct) ?? throw MiniAppProductionPolicy.Ended();
        return DocumentMetadata(id, result);
    }
    internal async Task<MiniAppProductionDocumentDto> ExternalTargetAsync(Guid id, string? credential, string? nonce, string? browser, CancellationToken ct)
    {
        var result = await RunAsync(Action.Context, id, credential, nonce, browser, ct) ?? throw MiniAppProductionPolicy.Ended();
        return DocumentMetadata(id, result);
    }
    private static MiniAppProductionDocumentDto DocumentMetadata(Guid id, RuntimeResult result)
    {
        var target = result.Target;
        return new(id, target.AppId, target.Target.Snapshot.Name, target.Website.AbsoluteUri, target.Website.GetLeftPart(UriPartial.Authority),
            target.Target.Snapshot.SsoEnabled, DateTime.SpecifyKind(result.ExpiresAt, DateTimeKind.Utc));
    }
    public async Task HeartbeatAsync(Guid id, string? credential, string? nonce, string? browser, CancellationToken ct) =>
        _ = await RunAsync(Action.Heartbeat, id, credential, nonce, browser, ct);
    public async Task CloseAsync(Guid id, string? credential, string? nonce, string? browser, CancellationToken ct) =>
        _ = await RunAsync(Action.Close, id, credential, nonce, browser, ct);
    public async Task RecordEventAsync(Guid id, string? credential, string? nonce, string? browser, MiniAppProductionEventCode code, CancellationToken ct)
    {
        if (code is not (MiniAppProductionEventCode.BridgeReady or MiniAppProductionEventCode.BridgeUnavailable or MiniAppProductionEventCode.PermissionDenied))
            throw new DeveloperAuthException(400, "INVALID_RUNTIME_EVENT", "Mã sự kiện không hợp lệ.");
        _ = await RunAsync(Action.Event, id, credential, nonce, browser, ct, code);
    }

    private async Task<RuntimeResult?> RunAsync(Action action, Guid id, string? credential, string? nonce, string? browser, CancellationToken ct, MiniAppProductionEventCode? code = null)
    {
        if (!options.Enabled) throw MiniAppProductionPolicy.Ended();
        if (id == Guid.Empty || !MiniAppProductionCredentials.Valid(credential) || (action != Action.Document && !MiniAppProductionCredentials.Valid(nonce))) throw Denied();
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Runtime action owns its transaction.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(action == Action.Close && db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, ct);
            // Close serializes on its own session only and can be repeated after its
            // scoped cookie was valid; it never consults or logs out a Social session.
            if (action == Action.Close) await LockSessionAsync(id, ct);
            var session = await db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (session == null || !MiniAppProductionCredentials.EqualHash(session.RuntimeCredentialHash, MiniAppProductionCredentials.Hash(credential!)) ||
                (action != Action.Document && !MiniAppProductionCredentials.EqualHash(session.BrowserNonceHash, MiniAppProductionCredentials.Hash(nonce!)))) throw Denied();
            if (session.ExpiresAt <= Now) throw MiniAppProductionPolicy.Ended();
            bindings.RequireBrowser(new(session.BindingId, session.BrowserBindingHash, session.BrowserNonceHash, session.ExpiresAt), browser);
            if (session.ClosedAt != null)
            {
                if (action != Action.Close) throw MiniAppProductionPolicy.Ended();
                await tx.CommitAsync(ct); return null;
            }
            if (action == Action.Close)
            {
                await CloseRowAsync(session, invalidated: false, ct); await tx.CommitAsync(ct); return null;
            }
            var actor = new MiniAppProductionActor(session.AccountId, session.SourceSocialSessionId);
            await policy.LockAsync(session.MiniAppId, actor, ct); await LockSessionAsync(id, ct);
            MiniAppProductionPolicyTarget current;
            try
            {
                await access.ValidateAsync(actor, ct);
                current = await policy.RequirePinnedAsync(session, ct);
            }
            catch (DeveloperAuthException)
            {
                await CloseRowAsync(session, invalidated: true, ct); await tx.CommitAsync(ct);
                throw MiniAppProductionPolicy.Ended();
            }
            if (session.ExpiresAt <= Now) throw MiniAppProductionPolicy.Ended();
            if (action == Action.Event)
            {
                var since = Now.AddMinutes(-1);
                if (await db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.HostSessionId == id && x.CreatedAt >= since &&
                    (x.Code == MiniAppProductionEventCode.BridgeReady || x.Code == MiniAppProductionEventCode.BridgeUnavailable || x.Code == MiniAppProductionEventCode.PermissionDenied)).Take(options.EventsPerSessionPerMinute).CountAsync(ct) >= options.EventsPerSessionPerMinute)
                    throw new DeveloperAuthException(429, "RUNTIME_EVENT_LIMIT", "Đã đạt giới hạn sự kiện Mini App.");
                audit.Append(session, code!.Value); await db.SaveChangesAsync(ct);
            }
            if (action == Action.Heartbeat && Now >= session.LastActivityAt.AddSeconds(options.HeartbeatSeconds))
            {
                var now = Now; var version = Guid.NewGuid();
                var changed = await db.Set<MiniAppProductionHostSession>().Where(x => x.Id == id && x.Version == session.Version && x.ClosedAt == null && x.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, now).SetProperty(x => x.Version, version), ct);
                if (changed != 1) throw MiniAppProductionPolicy.Ended();
                audit.Append(session, MiniAppProductionEventCode.Heartbeat); await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct); return new(current, session.ExpiresAt);
        }
        catch (Exception e) when (MiniAppVersionService.IsWriteConflict(e) || e is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.LockNotAvailable })
        { db.ChangeTracker.Clear(); throw MiniAppProductionPolicy.Ended(); }
    }

    private async Task CloseRowAsync(MiniAppProductionHostSession session, bool invalidated, CancellationToken ct)
    {
        var now = Now; var version = Guid.NewGuid();
        var query = db.Set<MiniAppProductionHostSession>().Where(x => x.Id == session.Id && x.Version == session.Version && x.ClosedAt == null);
        var changed = invalidated
            ? await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ClosedAt, now).SetProperty(x => x.RuntimeCredentialHash, (string?)null).SetProperty(x => x.Version, version), ct)
            : await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ClosedAt, now).SetProperty(x => x.Version, version), ct);
        if (changed != 1) throw MiniAppProductionPolicy.Ended();
        // A manually closed hash remains non-authorizing and supports idempotent
        // close retries. Context/heartbeat always reject ClosedAt; retention clears it.
        audit.Append(session, invalidated ? MiniAppProductionEventCode.Invalidated : MiniAppProductionEventCode.Closed);
        await db.SaveChangesAsync(ct);
    }
    private async Task LockSessionAsync(Guid id, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"MiniAppProductionHostSessions\" WHERE \"Id\"={id} FOR UPDATE", ct);
    }
    private static DeveloperAuthException Denied() => new(403, "HOST_AUTH_REQUIRED", "Không thể xác minh phiên Mini App.");
}
