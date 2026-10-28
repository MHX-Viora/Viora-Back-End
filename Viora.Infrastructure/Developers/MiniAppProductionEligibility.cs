using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppProductionTarget(MiniAppPublication Publication, MiniAppVersion Version,
    MiniAppVersionSnapshot Snapshot);

public static class MiniAppProductionEligibility
{
    // Shared SQL authority also lets the catalog filter before counting/paging.
    internal static IQueryable<MiniAppPublication> Publications(AppDbContext db, DateTime now) =>
        from publication in db.MiniAppPublications.AsNoTracking()
            join version in db.MiniAppVersions.AsNoTracking() on publication.MiniAppVersionId equals version.Id
            join review in db.MiniAppReviewRequests.AsNoTracking() on publication.ApprovedReviewRequestId equals review.Id
            join state in db.MiniAppOperationalStates.AsNoTracking() on publication.MiniAppId equals state.MiniAppId
            join app in db.MiniApps.AsNoTracking() on publication.MiniAppId equals app.Id
            where app.DeletedAt == null && app.Developer.Status == DeveloperStatus.Active
                && (app.Developer.LockedUntil == null || app.Developer.LockedUntil <= now)
                && publication.Status == MiniAppPublicationStatus.Published && publication.UnpublishedAt == null
                && publication.Environment == MiniAppOAuthEnvironment.Production && publication.PublishedAt <= now
                && publication.PublishedByDeveloperId == app.DeveloperAccountId
                && version.MiniAppId == app.Id && version.State != MiniAppVersionState.Draft
                && version.SubmittedAt != null && version.SnapshotSchemaVersion == 1
                && review.MiniAppId == app.Id && review.MiniAppVersionId == version.Id
                && review.DeveloperAccountId == app.DeveloperAccountId
                && review.Status == MiniAppReviewStatus.Approved && review.DecidedAt != null
                && state.Status == MiniAppOperationalStatus.Active
            select publication;

    internal static async Task<MiniAppProductionTarget> PublicationAsync(AppDbContext db, Guid appId, DateTime now, CancellationToken ct)
    {
        var row = await (from publication in Publications(db, now)
            join version in db.MiniAppVersions.AsNoTracking() on publication.MiniAppVersionId equals version.Id
            where publication.MiniAppId == appId
            select new { Publication = publication, Version = version }).SingleOrDefaultAsync(ct);
        if (row == null) throw Unavailable();
        var snapshot = MiniAppVersionSnapshotPolicy.Read(row.Version.SnapshotJson, row.Version.SnapshotHash).Snapshot;
        return new(row.Publication, row.Version, snapshot);
    }

    public static async Task<MiniAppProductionTarget> ReadAsync(AppDbContext db, Guid appId, DateTime now, CancellationToken ct)
    {
        try
        {
            var target = await PublicationAsync(db, appId, now, ct);
            var snapshot = target.Snapshot;
            if (snapshot.WebsiteUrl == null || snapshot.Domain == null ||
                !await db.MiniAppDomainVerifications.AsNoTracking().AnyAsync(x => x.MiniAppId == appId &&
                    x.Hostname == snapshot.Domain && x.Status == MiniAppDomainStatus.Verified && x.RevokedAt == null && x.ExpiresAt > now, ct))
                throw Unavailable();
            var current = await PublicationAsync(db, appId, now, ct);
            if (current.Publication.Id != target.Publication.Id || current.Publication.ConcurrencyToken != target.Publication.ConcurrencyToken ||
                current.Version.SnapshotHash != target.Version.SnapshotHash) throw Unavailable();
            return target;
        }
        catch (DeveloperAuthException) { throw Unavailable(); }
    }

    private static DeveloperAuthException Unavailable() => new(404, "APP_NOT_FOUND", "Mini App hiện không khả dụng.");
    private sealed class EvaluationClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(now, DateTimeKind.Utc));
    }
}
