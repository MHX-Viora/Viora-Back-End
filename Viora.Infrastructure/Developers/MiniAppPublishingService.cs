using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppPublishingService(AppDbContext db, MiniAppReviewAccess access, MiniAppReadinessService readiness, TimeProvider clock,
    MiniAppCatalogProjection? catalog = null)
{
    public async Task<MiniAppPublishResultDto> PublishAsync(Guid owner, Guid source, string appId, MiniAppPublishRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.ConcurrencyToken == Guid.Empty || request.VersionId == Guid.Empty ||
            request.PublicationId.HasValue != request.PublicationRevision.HasValue || request.PublicationId == Guid.Empty ||
            request.PublicationRevision == Guid.Empty || request.OperationalRevision == Guid.Empty) throw MiniAppInput.Invalid("Phiên bản phát hành không hợp lệ.");
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Developer, owner);
        var payload = MiniAppReviewAuditService.Hash(new { Command = "Publish", appId, request.ConcurrencyToken, request.VersionId,
            request.PublicationId, request.PublicationRevision, request.OperationalRevision });
        var audit = new MiniAppReviewAuditService(db);
        if (await Replay(audit, actor, app.Id, request.RequestId, payload, ct) is { } previous) return previous;
        var ticket = await readiness.PublicationAsync(owner, source, appId, request.VersionId, ct); var ready = ticket.Readiness;
        if (!ready.CanPublish || ready.AppVersion != request.ConcurrencyToken || ready.PublicationId != request.PublicationId ||
            ready.PublicationRevision != request.PublicationRevision || ready.OperationalRevision != request.OperationalRevision) throw Conflict();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            app = await access.RequireOwnerAsync(owner, source, appId, ct);
            if (await Replay(audit, actor, app.Id, request.RequestId, payload, ct) is { } replay) return replay;
            await readiness.RevalidatePublicationAsync(ticket, owner, source, appId, ct);
            var version = await db.MiniAppVersions.AsNoTracking().SingleAsync(x => x.Id == request.VersionId && x.MiniAppId == app.Id, ct);
            var safety = await readiness.TargetAsync(version, false, ct);
            var now = clock.GetUtcNow().UtcDateTime; var appRevision = Guid.NewGuid();
            if (await db.MiniApps.Where(x => x.Id == app.Id && x.DeveloperAccountId == owner && x.DeletedAt == null && x.Version == request.ConcurrencyToken)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, appRevision).SetProperty(x => x.Status, MiniAppStatus.Published).SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            if (request.PublicationId is { } oldId)
            {
                var closedRevision = Guid.NewGuid();
                if (await db.MiniAppPublications.Where(x => x.Id == oldId && x.MiniAppId == app.Id && x.Status == MiniAppPublicationStatus.Published && x.ConcurrencyToken == request.PublicationRevision)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppPublicationStatus.Archived).SetProperty(x => x.UnpublishedAt, (DateTime?)now)
                        .SetProperty(x => x.ConcurrencyToken, closedRevision), ct) != 1) throw Conflict();
            }
            var state = await db.MiniAppOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id, ct);
            if (state == null) { state = new() { MiniAppId = app.Id }; db.MiniAppOperationalStates.Add(state); }
            if (state.Status != MiniAppOperationalStatus.Active) throw Conflict();
            // Revoke old grants before replacing the library projection, including scope/type/callback changes.
            await new MiniAppBridgeRevocation(db, clock).RevokeProductionAsync(app.Id, ct);
            Guid? clientId = null;
            var publication = new MiniAppPublication { MiniAppId = app.Id, MiniAppVersionId = version.Id, ApprovedReviewRequestId = ready.ApprovedReviewRequestId!.Value,
                ProductionOAuthClientId = clientId, PublishedAt = now, PublishedByDeveloperId = owner };
            db.MiniAppPublications.Add(publication); await db.SaveChangesAsync(ct);
            if (catalog != null) await catalog.AppendAsync(publication, version, now, ct);
            var applied = await Applied(app, publication, version, appRevision, ct);
            var result = new MiniAppPublishResultDto(Map(publication, version.VersionCode), appRevision, state.ConcurrencyToken);
            var action = await audit.AppendAsync(new() { MiniAppId = app.Id, VersionId = version.Id, ReviewId = ready.ApprovedReviewRequestId, Actor = actor,
                Action = MiniAppReviewActionKind.Published, RequestId = request.RequestId, CorrelationId = request.RequestId, PayloadHash = payload,
                BeforeState = request.PublicationId == null ? "Unpublished" : "Published", AfterState = "Published",
                MetadataJson = JsonSerializer.Serialize(new { publicationId = publication.Id, previousPublicationId = request.PublicationId }), ResultJson = JsonSerializer.Serialize(result) }, ct);
            await new MiniAppReviewNotificationService(db, clock).QueueAsync(action.Id, publication.Id, ct);
            await access.RequireOwnerAsync(owner, source, appId, ct);
            if (await Applied(app, publication, version, appRevision, ct) != applied) throw Conflict();
            await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    public async Task<MiniAppPublishResultDto> UnpublishAsync(Guid owner, Guid source, string appId, MiniAppUnpublishRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.ConcurrencyToken == Guid.Empty || request.PublicationId == Guid.Empty || request.PublicationRevision == Guid.Empty ||
            request.OperationalRevision is null || request.OperationalRevision == Guid.Empty) throw MiniAppInput.Invalid("Phiên bản ngừng phát hành không hợp lệ.");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : MiniAppInput.Text(request.Reason, 2000, true);
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Developer, owner); var audit = new MiniAppReviewAuditService(db);
        var payload = MiniAppReviewAuditService.Hash(new { Command = "Unpublish", appId, request.ConcurrencyToken, request.PublicationId,
            request.PublicationRevision, request.OperationalRevision, reason });
        if (await Replay(audit, actor, app.Id, request.RequestId, payload, ct) is { } previous) return previous;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            app = await access.RequireOwnerAsync(owner, source, appId, ct);
            if (await Replay(audit, actor, app.Id, request.RequestId, payload, ct) is { } replay) return replay;
            var publication = await db.MiniAppPublications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PublicationId && x.MiniAppId == app.Id &&
                x.Status == MiniAppPublicationStatus.Published && x.ConcurrencyToken == request.PublicationRevision, ct) ?? throw Conflict();
            var state = await db.MiniAppOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id &&
                x.Status == MiniAppOperationalStatus.Active && x.ConcurrencyToken == request.OperationalRevision, ct) ?? throw Conflict();
            var now = clock.GetUtcNow().UtcDateTime; var revision = Guid.NewGuid(); var publicationRevision = Guid.NewGuid();
            if (await db.MiniApps.Where(x => x.Id == app.Id && x.DeveloperAccountId == owner && x.DeletedAt == null && x.Version == request.ConcurrencyToken && x.Status != MiniAppStatus.Suspended)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, revision).SetProperty(x => x.Status, MiniAppStatus.Draft).SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            if (await db.MiniAppPublications.Where(x => x.Id == publication.Id && x.Status == MiniAppPublicationStatus.Published && x.ConcurrencyToken == request.PublicationRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppPublicationStatus.UnpublishedByOwner).SetProperty(x => x.UnpublishedAt, (DateTime?)now)
                    .SetProperty(x => x.ConcurrencyToken, publicationRevision), ct) != 1) throw Conflict();
            await new MiniAppBridgeRevocation(db, clock).RevokeProductionAsync(app.Id, ct);
            publication.Status = MiniAppPublicationStatus.UnpublishedByOwner; publication.UnpublishedAt = now; publication.ConcurrencyToken = publicationRevision;
            var code = await db.MiniAppVersions.Where(x => x.Id == publication.MiniAppVersionId).Select(x => x.VersionCode).SingleAsync(ct);
            var result = new MiniAppPublishResultDto(Map(publication, code), revision, state.ConcurrencyToken);
            var action = await audit.AppendAsync(new() { MiniAppId = app.Id, VersionId = publication.MiniAppVersionId, ReviewId = publication.ApprovedReviewRequestId,
                Actor = actor, Action = MiniAppReviewActionKind.Unpublished, RequestId = request.RequestId, CorrelationId = request.RequestId, PayloadHash = payload,
                BeforeState = "Published", AfterState = "UnpublishedByOwner", Reason = reason,
                MetadataJson = JsonSerializer.Serialize(new { publicationId = publication.Id }), ResultJson = JsonSerializer.Serialize(result) }, ct);
            await new MiniAppReviewNotificationService(db, clock).QueueAsync(action.Id, publication.Id, ct);
            await access.RequireOwnerAsync(owner, source, appId, ct);
            if (!await db.MiniAppOperationalStates.AnyAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppOperationalStatus.Active && x.ConcurrencyToken == state.ConcurrencyToken, ct) ||
                !await db.MiniApps.AnyAsync(x => x.Id == app.Id && x.Version == revision && x.Status == MiniAppStatus.Draft, ct) ||
                await db.MiniAppPublications.AnyAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppPublicationStatus.Published, ct)) throw Conflict();
            await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    private async Task<string> Applied(MiniApp app, MiniAppPublication publication, MiniAppVersion version, Guid revision, CancellationToken ct)
    {
        var safety = await readiness.TargetAsync(version, false, ct);
        if (safety.BlockingReasons.Length != 0 || !await db.MiniApps.AnyAsync(x => x.Id == app.Id && x.Version == revision && x.Status == MiniAppStatus.Published, ct) ||
            !await db.MiniAppPublications.AnyAsync(x => x.Id == publication.Id && x.Status == MiniAppPublicationStatus.Published && x.ConcurrencyToken == publication.ConcurrencyToken, ct) ||
            !await db.MiniAppOperationalStates.AnyAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppOperationalStatus.Active, ct)) throw Conflict();
        var review = await db.MiniAppReviewRequests.AsNoTracking().SingleAsync(x => x.Id == publication.ApprovedReviewRequestId, ct);
        if (review.Status != MiniAppReviewStatus.Approved || review.DecidedAt == null) throw Conflict();
        MiniAppReviewEvidencePolicy.Read(review, version);
        return MiniAppReviewAuditService.Hash(new { safety.Fingerprint, review.ConcurrencyToken, review.EvidenceAttestationJson });
    }

    public async Task<MiniAppPublicationPageDto> ListAsync(Guid owner, Guid source, string appId, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100) throw MiniAppInput.Invalid("Phân trang không hợp lệ.");
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var query = db.MiniAppPublications.AsNoTracking().Where(x => x.MiniAppId == app.Id);
        var total = await query.CountAsync(ct);
        var rows = await (from publication in query join version in db.MiniAppVersions on publication.MiniAppVersionId equals version.Id
            orderby publication.PublishedAt descending, publication.Id descending select new { Publication = publication, version.VersionCode })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(x => Map(x.Publication, x.VersionCode)).ToArray(), page, pageSize, total);
    }
    internal static MiniAppPublicationDto Map(MiniAppPublication p, long code) => new(p.Id, p.MiniAppVersionId, code, p.ApprovedReviewRequestId,
        p.Status.ToString(), p.PublishedAt, p.UnpublishedAt, p.ConcurrencyToken);
    private static async Task<MiniAppPublishResultDto?> Replay(MiniAppReviewAuditService audit, MiniAppReviewAuditActor actor, Guid app, Guid request, string payload, CancellationToken ct)
    {
        var previous = await audit.FindAsync(actor, app, request, payload, ct); if (previous == null) return null;
        try { return JsonSerializer.Deserialize<MiniAppPublishResultDto>(previous.ResultJson) ?? throw Conflict(); }
        catch (JsonException) { throw Conflict(); }
    }
    internal static DeveloperAuthException Conflict() => new(409, "PUBLICATION_CONFLICT", "Điều kiện phát hành đã thay đổi. Hãy kiểm tra lại phiên bản và readiness.");
}
