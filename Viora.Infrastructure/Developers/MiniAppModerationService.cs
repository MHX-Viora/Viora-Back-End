using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppModerationService(AppDbContext db, MiniAppReviewAccess access, TimeProvider clock, MiniAppReadinessService readiness,
    MiniAppBridgeRevocation? revocation = null)
{
    public Task<MiniAppModerationResultDto> SuspendAsync(Guid admin, Guid source, string appId, MiniAppModerationRequest request, CancellationToken ct) =>
        ChangeAsync(admin, source, appId, request, false, ct);
    public Task<MiniAppModerationResultDto> RestoreAsync(Guid admin, Guid source, string appId, MiniAppModerationRequest request, CancellationToken ct) =>
        ChangeAsync(admin, source, appId, request, true, ct);

    private async Task<MiniAppModerationResultDto> ChangeAsync(Guid admin, Guid source, string appId, MiniAppModerationRequest request, bool restore, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.ConcurrencyToken == Guid.Empty || request.PublicationId.HasValue != request.PublicationRevision.HasValue ||
            request.PublicationId == Guid.Empty || request.PublicationRevision == Guid.Empty || request.OperationalRevision == Guid.Empty) throw MiniAppInput.Invalid("Phiên bản vận hành không hợp lệ.");
        var reason = MiniAppInput.Text(request.Reason, 2000, true); if (reason.Length < 10) throw MiniAppInput.Invalid("Lý do phải có từ 10 đến 2000 ký tự.");
        await access.RequireAdminAsync(admin, source, ct);
        var app = await App(appId, ct); var audit = new MiniAppReviewAuditService(db); var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Admin, admin);
        var kind = restore ? MiniAppReviewActionKind.Restored : MiniAppReviewActionKind.Suspended;
        var payload = MiniAppReviewAuditService.Hash(new { Command = kind.ToString(), appId, request.ConcurrencyToken, request.OperationalRevision, request.PublicationId, request.PublicationRevision, reason });
        if (await Replay(audit, actor, app.Id, request.RequestId, payload, ct) is { } previous) return previous;
        string? restoreSafety = null;
        if (restore) { await Current(app, request, true, ct); restoreSafety = await RestorationSafety(app.Id, true, ct); }
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await access.RequireAdminAsync(admin, source, ct); app = await App(appId, ct);
            if (await Replay(audit, actor, app.Id, request.RequestId, payload, ct) is { } replay) return replay;
            var (state, publication) = await Current(app, request, restore, ct);
            if (restore && await RestorationSafety(app.Id, false, ct) != restoreSafety) throw Conflict();
            var now = clock.GetUtcNow().UtcDateTime; var revision = Guid.NewGuid(); var stateRevision = Guid.NewGuid();
            var status = restore ? MiniAppOperationalStatus.Active : MiniAppOperationalStatus.Suspended;
            if (await db.MiniApps.Where(x => x.Id == app.Id && x.DeletedAt == null && x.Version == request.ConcurrencyToken)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, revision).SetProperty(x => x.Status, restore ? MiniAppStatus.Draft : MiniAppStatus.Suspended)
                    .SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            if (state == null)
            {
                state = new() { MiniAppId = app.Id, Status = status, Reason = reason, ChangedByAdminAccountId = admin, SuspendedAt = now, ConcurrencyToken = stateRevision };
                db.MiniAppOperationalStates.Add(state);
            }
            else if (await db.MiniAppOperationalStates.Where(x => x.Id == state.Id && x.ConcurrencyToken == request.OperationalRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.Reason, reason).SetProperty(x => x.ChangedByAdminAccountId, (Guid?)admin)
                    .SetProperty(x => x.SuspendedAt, restore ? (DateTime?)null : now).SetProperty(x => x.ConcurrencyToken, stateRevision).SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            if (publication != null)
            {
                var pubRevision = Guid.NewGuid();
                if (await db.MiniAppPublications.Where(x => x.Id == publication.Id && x.Status == MiniAppPublicationStatus.Published && x.ConcurrencyToken == request.PublicationRevision)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppPublicationStatus.Suspended).SetProperty(x => x.UnpublishedAt, (DateTime?)now)
                        .SetProperty(x => x.SuspendedAt, (DateTime?)now).SetProperty(x => x.SuspensionReason, reason).SetProperty(x => x.ConcurrencyToken, pubRevision), ct) != 1) throw Conflict();
                publication.Status = MiniAppPublicationStatus.Suspended; publication.UnpublishedAt = publication.SuspendedAt = now;
                publication.SuspensionReason = reason; publication.ConcurrencyToken = pubRevision;
            }
            if (!restore) { await new MiniAppBridgeRevocation(db, clock).RevokeAllAsync(app.Id, ct); await CloseSandbox(app.Id, now, ct); }
            await db.SaveChangesAsync(ct);
            var afterSafety = restore ? await RestorationSafety(app.Id, false, ct) : null;
            var code = publication == null ? 0 : await db.MiniAppVersions.Where(x => x.Id == publication.MiniAppVersionId).Select(x => x.VersionCode).SingleAsync(ct);
            var result = new MiniAppModerationResultDto(new(status.ToString(), reason, restore ? null : now, stateRevision), revision,
                publication == null ? null : MiniAppPublishingService.Map(publication, code));
            var action = await audit.AppendAsync(new() { MiniAppId = app.Id, VersionId = publication?.MiniAppVersionId, ReviewId = publication?.ApprovedReviewRequestId,
                Actor = actor, Action = kind, RequestId = request.RequestId, CorrelationId = request.RequestId, PayloadHash = payload,
                BeforeState = restore ? "Suspended" : "Active", AfterState = status.ToString(), Reason = reason,
                MetadataJson = JsonSerializer.Serialize(new { publicationId = publication?.Id }), ResultJson = JsonSerializer.Serialize(result) }, ct);
            await new MiniAppReviewNotificationService(db, clock).QueueAsync(action.Id, publication?.Id, ct);
            await access.RequireAdminAsync(admin, source, ct);
            if (!await db.MiniApps.AnyAsync(x => x.Id == app.Id && x.Version == revision && x.Status == (restore ? MiniAppStatus.Draft : MiniAppStatus.Suspended), ct) ||
                !await db.MiniAppOperationalStates.AnyAsync(x => x.MiniAppId == app.Id && x.Status == status && x.ConcurrencyToken == stateRevision, ct) ||
                await db.MiniAppPublications.AnyAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppPublicationStatus.Published, ct)) throw Conflict();
            if (restore && await RestorationSafety(app.Id, false, ct) != afterSafety) throw Conflict();
            await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    private async Task<MiniApp> App(string appId, CancellationToken ct) => await db.MiniApps.AsNoTracking()
        .SingleOrDefaultAsync(x => x.AppId == appId && x.DeletedAt == null, ct) ?? throw MiniAppReviewAccess.NotFound();
    private async Task<(MiniAppOperationalState? State, MiniAppPublication? Publication)> Current(MiniApp app, MiniAppModerationRequest request, bool restore, CancellationToken ct)
    {
        var state = await db.MiniAppOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id, ct);
        var publication = await db.MiniAppPublications.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppPublicationStatus.Published, ct);
        if (app.Version != request.ConcurrencyToken || state?.ConcurrencyToken != request.OperationalRevision || publication?.Id != request.PublicationId ||
            publication?.ConcurrencyToken != request.PublicationRevision || (restore ? state?.Status != MiniAppOperationalStatus.Suspended || publication != null : state?.Status == MiniAppOperationalStatus.Suspended)) throw Conflict();
        return (state, publication);
    }
    private async Task<string> RestorationSafety(Guid appId, bool probe, CancellationToken ct)
    {
        var app = await db.MiniApps.AsNoTracking().Include(x => x.Developer).SingleAsync(x => x.Id == appId && x.DeletedAt == null, ct);
        if (app.Developer.Status != DeveloperStatus.Active || app.Developer.LockedUntil > clock.GetUtcNow().UtcDateTime) throw Conflict();
        var versionId = await db.MiniAppPublications.Where(x => x.MiniAppId == appId && x.Status == MiniAppPublicationStatus.Suspended)
            .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Select(x => (Guid?)x.MiniAppVersionId).FirstOrDefaultAsync(ct)
            ?? await db.MiniAppReviewRequests.Where(x => x.MiniAppId == appId && x.Status == MiniAppReviewStatus.Approved)
                .OrderByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).Select(x => (Guid?)x.MiniAppVersionId).FirstOrDefaultAsync(ct);
        if (versionId == null) return MiniAppReviewAuditService.Hash(new { app.DeveloperAccountId, app.Developer.SecurityVersion, app.Developer.Status, app.Developer.LockedUntil });
        var version = await db.MiniAppVersions.AsNoTracking().SingleAsync(x => x.Id == versionId && x.MiniAppId == appId, ct);
        var review = await db.MiniAppReviewRequests.AsNoTracking().SingleAsync(x => x.MiniAppId == appId && x.MiniAppVersionId == versionId && x.Status == MiniAppReviewStatus.Approved, ct);
        MiniAppReviewEvidencePolicy.Read(review, version);
        var safety = await readiness.TargetAsync(version, probe, ct);
        if (safety.BlockingReasons.Any(x => x != "APP_SUSPENDED")) throw Conflict();
        return MiniAppReviewAuditService.Hash(new { safety.Fingerprint, review.ConcurrencyToken, review.EvidenceAttestationJson });
    }
    private async Task CloseSandbox(Guid appId, DateTime now, CancellationToken ct)
    {
        var revision = Guid.NewGuid(); var sessions = db.MiniAppSandboxSessions.Where(x => x.MiniAppId == appId);
        await sessions.Where(x => x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTime?)now)
            .SetProperty(x => x.RuntimeCredentialHash, (string?)null).SetProperty(x => x.Version, revision), ct);
        await db.MiniAppSandboxLaunchGrants.Where(x => sessions.Select(y => y.Id).Contains(x.SandboxSessionId) && x.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, (DateTime?)now).SetProperty(x => x.Version, revision), ct);
        var runs = db.MiniAppSandboxTestRuns.Where(x => sessions.Select(y => y.Id).Contains(x.SandboxSessionId) && (x.Status == SandboxTestRunStatus.Queued || x.Status == SandboxTestRunStatus.Running));
        var runIds = await runs.Select(x => x.Id).ToArrayAsync(ct);
        await runs.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SandboxTestRunStatus.Cancelled).SetProperty(x => x.SummaryCode, SandboxMessageCode.RunCancelled)
            .SetProperty(x => x.CompletedAt, x => x.StartedAt != null && x.StartedAt > now ? x.StartedAt : x.CreatedAt > now ? x.CreatedAt : now).SetProperty(x => x.Version, revision), ct);
        await db.MiniAppSandboxTestResults.Where(x => runIds.Contains(x.TestRunId) && x.MessageCode == SandboxMessageCode.CheckPending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SandboxCheckStatus.Skipped).SetProperty(x => x.MessageCode, SandboxMessageCode.MissingEvidence).SetProperty(x => x.Version, revision), ct);
    }
    private static async Task<MiniAppModerationResultDto?> Replay(MiniAppReviewAuditService audit, MiniAppReviewAuditActor actor, Guid app, Guid request, string payload, CancellationToken ct)
    {
        var previous = await audit.FindAsync(actor, app, request, payload, ct); if (previous == null) return null;
        try { return JsonSerializer.Deserialize<MiniAppModerationResultDto>(previous.ResultJson) ?? throw Conflict(); }
        catch (JsonException) { throw Conflict(); }
    }
    private static DeveloperAuthException Conflict() => new(409, "MODERATION_CONFLICT", "Trạng thái hoặc điều kiện vận hành đã thay đổi. Hãy kiểm tra lại.");
}
