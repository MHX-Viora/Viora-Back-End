using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppProductionPolicyTarget(MiniAppProductionTarget Target, string Fingerprint, Guid OperationalRevision, Uri Website, string AppId);

public sealed class MiniAppProductionPolicy(AppDbContext db, TimeProvider clock, MiniAppProductionOptions options, bool bridgeEnabled = false)
{
    public async Task LockAsync(Guid appId, MiniAppProductionActor actor, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Authority locks require the action transaction.");
        if (!db.Database.IsNpgsql()) return;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM "MiniApps" a JOIN "DeveloperAccounts" d ON d."Id"=a."DeveloperAccountId"
                WHERE a."Id"={appId} FOR SHARE OF a,d;
            SELECT 1 FROM "MiniAppOperationalStates" o WHERE o."MiniAppId"={appId} FOR SHARE OF o;
            SELECT 1 FROM "MiniAppPublications" p JOIN "MiniAppVersions" v ON v."Id"=p."MiniAppVersionId"
                JOIN "MiniAppReviewRequests" r ON r."Id"=p."ApprovedReviewRequestId"
                WHERE p."MiniAppId"={appId} AND p."Status"=0 FOR SHARE OF p,v,r;
            SELECT 1 FROM "MiniAppDomainVerifications" d WHERE d."MiniAppId"={appId} AND d."RevokedAt" IS NULL FOR SHARE OF d;
            """, ct);
        if (actor.AccountId != null && actor.SourceSocialSessionId != null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT 1 FROM "Accounts" a WHERE a."Id"={actor.AccountId.Value} FOR SHARE OF a;
                SELECT 1 FROM "RefreshTokens" r WHERE r."AccountId"={actor.AccountId.Value}
                    AND r."SessionId"={actor.SourceSocialSessionId.Value} AND r."RevokedAt" IS NULL FOR SHARE OF r;
                """, ct);
    }

    public async Task<MiniAppProductionPolicyTarget> ReadAsync(Guid appId, CancellationToken ct)
    {
        if (!options.Enabled) throw Unavailable();
        var now = clock.GetUtcNow().UtcDateTime;
        var target = await MiniAppProductionEligibility.ReadAsync(db, appId, now, ct);
        var website = new Uri(PublicWebAddress.Normalize(target.Snapshot.WebsiteUrl!));
        if (!Uri.TryCreate(options.HostOrigin, UriKind.Absolute, out var host) ||
            host.IdnHost.TrimEnd('.').Equals(website.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) || options.IsReservedHost(website.IdnHost)) throw Unavailable();
        var owner = await db.MiniApps.AsNoTracking().Where(x => x.Id == appId).Select(x => new
        { x.AppId, x.DeveloperAccountId, x.Developer.SecurityVersion, x.Developer.Status, x.Developer.LockedUntil }).SingleAsync(ct);
        var state = await db.MiniAppOperationalStates.AsNoTracking().SingleAsync(x => x.MiniAppId == appId, ct);
        var proof = await db.MiniAppDomainVerifications.AsNoTracking().Where(x => x.MiniAppId == appId && x.Hostname == target.Snapshot.Domain &&
            x.Status == MiniAppDomainStatus.Verified && x.RevokedAt == null && x.ExpiresAt > now)
            .Select(x => new { x.Id, x.Version, x.ExpiresAt }).SingleOrDefaultAsync(ct) ?? throw Unavailable();
        var fingerprint = MiniAppReviewAuditService.Hash(new { target.Publication.Id, target.Publication.ConcurrencyToken,
            target.Version.SnapshotHash, owner, OperationalRevision = state.ConcurrencyToken, state.Status, proof });
        if (state.Status != MiniAppOperationalStatus.Active) throw Unavailable();
        return new(target, fingerprint, state.ConcurrencyToken, website, owner.AppId);
    }
    public async Task<MiniAppProductionPolicyTarget> RequirePinnedAsync(MiniAppProductionHostSession session, CancellationToken ct)
    {
        MiniAppProductionPolicyTarget current;
        try { current = await ReadAsync(session.MiniAppId, ct); }
        catch (DeveloperAuthException) { throw Ended(); }
        var target = current.Target;
        if (target.Publication.Id != session.PublicationId || target.Version.Id != session.MiniAppVersionId ||
            target.Publication.ApprovedReviewRequestId != session.ApprovedReviewRequestId || target.Version.SnapshotHash != session.SnapshotHash ||
            target.Publication.ConcurrencyToken != session.PublicationRevision || current.OperationalRevision != session.OperationalRevision ||
            current.Fingerprint != session.PolicyFingerprint) throw Ended();
        return current;
    }
    internal static DeveloperAuthException Ended() => new(410, "HOST_SESSION_ENDED", "Phiên Mini App đã kết thúc. Vui lòng mở lại.");
    private static DeveloperAuthException Unavailable() => new(404, "APP_NOT_FOUND", "Mini App hiện không khả dụng.");
}
