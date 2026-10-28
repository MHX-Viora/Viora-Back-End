using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppReviewService(AppDbContext db, MiniAppReviewAccess access)
{
    public async Task<MiniAppAdminAppPageDto> ListAdminAppsAsync(Guid admin, Guid source, string? search, int page, int pageSize, CancellationToken ct, string? view = null)
    {
        await access.RequireAdminAsync(admin, source, ct); search = search?.Trim();
        if (search?.Length > 128 || search?.Any(char.IsControl) == true || page is < 1 or > 100000 || pageSize is < 1 or > 100 || view is not (null or "published" or "suspended")) throw InvalidQuery();
        var query = db.MiniApps.AsNoTracking().Where(x => x.DeletedAt == null && (search == null || search == "" || x.AppId.Contains(search) || x.Name.Contains(search)));
        if (view == "published") query = query.Where(x => db.MiniAppPublications.Any(p => p.MiniAppId == x.Id && p.Status == MiniAppPublicationStatus.Published));
        if (view == "suspended") query = query.Where(x => db.MiniAppOperationalStates.Any(s => s.MiniAppId == x.Id && s.Status == MiniAppOperationalStatus.Suspended));
        var total = await query.CountAsync(ct);
        var apps = await query.Include(x => x.Developer).OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = apps.Select(x => x.Id).ToArray();
        var states = await db.MiniAppOperationalStates.AsNoTracking().Where(x => ids.Contains(x.MiniAppId)).ToDictionaryAsync(x => x.MiniAppId, ct);
        var publications = await db.MiniAppPublications.AsNoTracking().Where(x => ids.Contains(x.MiniAppId) && x.Status == MiniAppPublicationStatus.Published).ToDictionaryAsync(x => x.MiniAppId, ct);
        return new(apps.Select(x => AppSummary(x, states.GetValueOrDefault(x.Id), publications.GetValueOrDefault(x.Id))).ToArray(), page, pageSize, total);
    }
    public async Task<MiniAppAdminAppDetailDto> GetAdminAppAsync(Guid admin, Guid source, string appId, CancellationToken ct)
    {
        await access.RequireAdminAsync(admin, source, ct);
        var app = await db.MiniApps.AsNoTracking().Include(x => x.Developer).SingleOrDefaultAsync(x => x.AppId == appId && x.DeletedAt == null, ct) ?? throw MiniAppReviewAccess.NotFound();
        var state = await db.MiniAppOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id, ct);
        var live = await db.MiniAppPublications.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppPublicationStatus.Published, ct);
        var versions = await db.MiniAppVersions.AsNoTracking().Where(x => x.MiniAppId == app.Id).OrderByDescending(x => x.VersionCode).Take(20)
            .Select(x => new MiniAppVersionSummaryDto(x.Id, x.VersionCode, x.VersionName, x.State.ToString(), x.SnapshotHash, x.ConcurrencyToken, x.CreatedAt, x.UpdatedAt, x.SubmittedAt)).ToListAsync(ct);
        var publications = await db.MiniAppPublications.AsNoTracking().Where(x => x.MiniAppId == app.Id).OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Take(20)
            .Select(x => new MiniAppAdminPublicationDto(x.Id, x.MiniAppVersionId, x.ApprovedReviewRequestId, x.Status.ToString(), x.PublishedAt, x.UnpublishedAt, x.ConcurrencyToken, x.SuspensionReason)).ToListAsync(ct);
        MiniAppVersionSnapshot? snapshot = null;
        if (live != null)
        {
            var version = await db.MiniAppVersions.AsNoTracking().SingleAsync(x => x.Id == live.MiniAppVersionId && x.MiniAppId == app.Id, ct);
            snapshot = MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot;
        }
        return new(AppSummary(app, state, live), versions, publications, snapshot);
    }
    private static MiniAppAdminAppSummaryDto AppSummary(MiniApp app, MiniAppOperationalState? state, MiniAppPublication? publication) => new(app.AppId,
        app.Name, app.Developer.FullName, app.Developer.EmailNormalized, app.Status.ToString(), state?.Status.ToString() ?? "NotInitialized",
        app.Version, state?.ConcurrencyToken, publication?.Id, publication?.MiniAppVersionId,
        state?.Status != MiniAppOperationalStatus.Suspended, state?.Status == MiniAppOperationalStatus.Suspended, state?.Reason);

    public async Task<MiniAppReviewAdminPageDto> ListAdminAsync(Guid admin, Guid source, MiniAppReviewAdminQuery request, CancellationToken ct)
    {
        await access.RequireAdminAsync(admin, source, ct);
        MiniAppReviewStatus? status = null;
        if (request.Status != null)
        {
            if (!Enum.TryParse<MiniAppReviewStatus>(request.Status, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != request.Status) throw InvalidQuery();
            status = parsed;
        }
        var search = request.Search?.Trim(); var sort = request.Sort ?? "oldest";
        if (search?.Length > 128 || search?.Any(char.IsControl) == true || request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 100 ||
            request.Assignee == Guid.Empty || sort is not ("oldest" or "newest")) throw InvalidQuery();
        var query = from review in db.MiniAppReviewRequests.AsNoTracking()
            join app in db.MiniApps.AsNoTracking() on review.MiniAppId equals app.Id
            join version in db.MiniAppVersions.AsNoTracking() on review.MiniAppVersionId equals version.Id
            join developer in db.DeveloperAccounts.AsNoTracking() on review.DeveloperAccountId equals developer.Id
            where app.DeletedAt == null && (status == null || review.Status == status) && (request.Assignee == null || review.AssignedAdminAccountId == request.Assignee)
                && (search == null || search == "" || app.AppId.Contains(search) || version.VersionName.Contains(search) || version.SnapshotJson.Contains(search))
            select new { Review = review, Version = version, app.AppId, DeveloperName = developer.FullName, DeveloperEmail = developer.EmailNormalized };
        var total = await query.CountAsync(ct);
        var ordered = sort == "oldest" ? query.OrderBy(x => x.Review.SubmittedAt).ThenBy(x => x.Review.Id)
            : query.OrderByDescending(x => x.Review.SubmittedAt).ThenByDescending(x => x.Review.Id);
        var rows = await ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(rows.Select(x => {
            var snapshot = MiniAppVersionSnapshotPolicy.Read(x.Version.SnapshotJson, x.Version.SnapshotHash).Snapshot;
            return new MiniAppReviewAdminListItem(Summary(x.Review, x.Version.VersionCode), x.AppId,
                snapshot.Name, x.Version.VersionName, x.DeveloperName, x.DeveloperEmail, x.Review.AssignedAdminAccountId, snapshot.LogoUrl);
        }).ToArray(), request.Page, request.PageSize, total);
    }

    public async Task<MiniAppReviewAdminOverviewDto> OverviewAdminAsync(Guid admin, Guid source, CancellationToken ct)
    {
        await access.RequireAdminAsync(admin, source, ct);
        var rows = await db.MiniAppReviewRequests.AsNoTracking().Where(x => db.MiniApps.Any(a => a.Id == x.MiniAppId && a.DeletedAt == null))
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        var counts = Enum.GetValues<MiniAppReviewStatus>().ToDictionary(x => x.ToString(), x => rows.FirstOrDefault(r => r.Status == x)?.Count ?? 0);
        return new(counts, await db.MiniAppPublications.CountAsync(x => x.Status == MiniAppPublicationStatus.Published && db.MiniApps.Any(a => a.Id == x.MiniAppId && a.DeletedAt == null), ct),
            await db.MiniAppOperationalStates.CountAsync(x => x.Status == MiniAppOperationalStatus.Suspended && db.MiniApps.Any(a => a.Id == x.MiniAppId && a.DeletedAt == null), ct));
    }

    public async Task<MiniAppReviewAdminDetailDto> GetAdminAsync(Guid admin, Guid source, Guid reviewId, CancellationToken ct)
    {
        await access.RequireAdminAsync(admin, source, ct);
        var review = await db.MiniAppReviewRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reviewId, ct) ?? throw MiniAppReviewAccess.NotFound();
        var app = await db.MiniApps.AsNoTracking().Include(x => x.Developer).SingleOrDefaultAsync(x => x.Id == review.MiniAppId && x.DeletedAt == null, ct) ?? throw MiniAppReviewAccess.NotFound();
        var suspended = await db.MiniAppOperationalStates.AnyAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppOperationalStatus.Suspended, ct);
        return new(await DetailAsync(review, ct), app.AppId, app.Developer.FullName, app.Developer.EmailNormalized, review.AssignedAdminAccountId,
            !suspended && review.Status == MiniAppReviewStatus.PendingReview, !suspended && review.Status == MiniAppReviewStatus.InReview,
            !suspended && review.Status == MiniAppReviewStatus.InReview && review.AssignedAdminAccountId == admin, !suspended);
    }
    private static DeveloperAuthException InvalidQuery() => new(400, "REVIEW_QUERY_INVALID", "Bộ lọc hoặc phân trang không hợp lệ.");

    public async Task<MiniAppReviewPageDto> ListOwnedAsync(Guid owner, Guid source, string appId, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100) throw MiniAppInput.Invalid("Phân trang không hợp lệ.");
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var query = from review in db.MiniAppReviewRequests.AsNoTracking()
            join version in db.MiniAppVersions.AsNoTracking() on review.MiniAppVersionId equals version.Id
            where review.MiniAppId == app.Id select new { Review = review, version.VersionCode };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Review.SubmittedAt).ThenByDescending(x => x.Review.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(x => Summary(x.Review, x.VersionCode)).ToArray(), page, pageSize, total);
    }
    public async Task<MiniAppReviewDetailDto> GetOwnedAsync(Guid owner, Guid source, string appId, Guid reviewId, CancellationToken ct)
    {
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var review = await db.MiniAppReviewRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reviewId && x.MiniAppId == app.Id, ct) ?? throw MiniAppReviewAccess.NotFound();
        return await DetailAsync(review, ct);
    }
    internal async Task<MiniAppReviewDetailDto> DetailAsync(MiniAppReviewRequest review, CancellationToken ct)
    {
        var version = await db.MiniAppVersions.AsNoTracking().SingleAsync(x => x.Id == review.MiniAppVersionId && x.MiniAppId == review.MiniAppId, ct);
        var snapshot = MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot;
        var evidence = MiniAppReviewEvidencePolicy.Read(review, version);
        MiniAppReviewChangeItem[] changes;
        try
        {
            changes = JsonSerializer.Deserialize<MiniAppReviewChangeItem[]>(review.ChangeRequestsJson,
                new JsonSerializerOptions { MaxDepth = 4, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw MiniAppReviewEvidencePolicy.Invalid();
            if (changes.Length > 20 || changes.Any(x => x == null || string.IsNullOrWhiteSpace(x.Field) || x.Field.Length > 64 ||
                string.IsNullOrWhiteSpace(x.Message) || x.Message.Length > 2000)) throw MiniAppReviewEvidencePolicy.Invalid();
        }
        catch (JsonException) { throw MiniAppReviewEvidencePolicy.Invalid(); }
        var actions = await db.MiniAppReviewActions.AsNoTracking().Where(x => x.ReviewRequestId == review.Id && x.MiniAppId == review.MiniAppId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(100)
            .Select(x => new MiniAppReviewActionDto(x.Action.ToString(), x.ActorType.ToString(), x.BeforeState, x.AfterState, x.Reason, x.CreatedAt)).ToListAsync(ct);
        return new(Summary(review, version.VersionCode), version.VersionName, version.SnapshotHash, snapshot, evidence, review.DecisionReason, changes, actions);
    }
    internal static MiniAppReviewSummaryDto Summary(MiniAppReviewRequest review, long versionCode) => new(review.Id, review.MiniAppVersionId, versionCode,
        review.Status.ToString(), review.SubmittedAt, review.ReviewStartedAt, review.DecidedAt, review.ConcurrencyToken);
}
