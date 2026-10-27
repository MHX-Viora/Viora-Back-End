using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppReviewSubmissionService(AppDbContext db, MiniAppReviewAccess access, MiniAppReadinessService readiness, TimeProvider clock)
{
    public async Task<MiniAppReviewSummaryDto> SubmitAsync(Guid owner, Guid source, string appId, MiniAppReviewSubmitRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.VersionId == Guid.Empty || request.ConcurrencyToken == Guid.Empty || request.AppVersion == Guid.Empty)
            throw MiniAppInput.Invalid("Mã yêu cầu và phiên bản không hợp lệ.");
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Developer, owner); var audit = new MiniAppReviewAuditService(db);
        var payload = MiniAppReviewAuditService.Hash(new { Command = "review.submit", appId, request.VersionId, request.ConcurrencyToken, request.AppVersion });
        if (await Replay(audit, actor, app.Id, request, payload, ct) is { } replay) return replay;
        var expected = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.VersionId && x.MiniAppId == app.Id, ct) ?? throw MiniAppReviewAccess.NotFound();
        if (app.Version != request.AppVersion || expected.State != MiniAppVersionState.Draft || expected.ConcurrencyToken != request.ConcurrencyToken) throw MiniAppReadinessService.Changed();
        // All network work ends before the serializable business transaction starts.
        var ticket = await readiness.PrepareAsync(owner, source, appId, request.VersionId, ct);
        if (!ticket.Readiness.ReadyForReview) throw MiniAppReadinessService.Blocked();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            app = await access.RequireOwnerAsync(owner, source, appId, ct);
            if (await Replay(audit, actor, app.Id, request, payload, ct) is { } repeated) return repeated;
            await readiness.RevalidateAsync(ticket, owner, source, appId, ct);
            if (await db.MiniAppReviewRequests.AnyAsync(x => x.MiniAppId == app.Id && (x.Status == MiniAppReviewStatus.PendingReview || x.Status == MiniAppReviewStatus.InReview), ct))
                throw new DeveloperAuthException(409, "REVIEW_ALREADY_ACTIVE", "Mini App đã có phiên bản đang xét duyệt.");
            var now = clock.GetUtcNow().UtcDateTime;
            // Lock the app against concurrent metadata writes without invalidating the evidence fingerprint.
            // Submission changes version lifecycle, not the reviewed working configuration.
            if (await db.MiniApps.Where(x => x.Id == app.Id && x.Version == request.AppVersion && x.DeletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, now), ct) != 1) throw MiniAppReadinessService.Changed();
            var revision = Guid.NewGuid();
            if (await db.MiniAppVersions.Where(x => x.Id == request.VersionId && x.MiniAppId == app.Id && x.State == MiniAppVersionState.Draft
                && x.ConcurrencyToken == request.ConcurrencyToken && x.SnapshotHash == ticket.Readiness.SnapshotHash).ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.State, MiniAppVersionState.Submitted).SetProperty(x => x.SubmittedAt, now)
                        .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ConcurrencyToken, revision), ct) != 1) throw MiniAppReadinessService.Changed();
            var ready = ticket.Readiness;
            var attestation = JsonSerializer.Serialize(new MiniAppReviewEvidenceAttestation(ticket.EvidenceSchemaVersion, request.VersionId, ready.SnapshotHash,
                ready.SandboxRunId!.Value, ready.ConfigurationFingerprint!, ready.EvaluatedAt, ready.RequiredChecks));
            if (Encoding.UTF8.GetByteCount(attestation) > 32768) throw MiniAppReadinessService.Blocked();
            var review = new MiniAppReviewRequest { MiniAppId = app.Id, MiniAppVersionId = request.VersionId, DeveloperAccountId = owner,
                Status = MiniAppReviewStatus.PendingReview, SubmittedAt = now, EvidenceAttestationJson = attestation, CorrelationId = request.RequestId };
            db.MiniAppReviewRequests.Add(review); await db.SaveChangesAsync(ct);
            var result = new MiniAppReviewSummaryDto(review.Id, request.VersionId, expected.VersionCode, review.Status.ToString(), now, null, null, review.ConcurrencyToken);
            var action = await audit.AppendAsync(new() { MiniAppId = app.Id, VersionId = request.VersionId, ReviewId = review.Id, Actor = actor,
                Action = MiniAppReviewActionKind.ReviewSubmitted, BeforeState = "Draft", AfterState = "PendingReview", RequestId = request.RequestId,
                CorrelationId = request.RequestId, PayloadHash = payload, ResultJson = JsonSerializer.Serialize(result) }, ct);
            await new MiniAppReviewNotificationService(db, clock).QueueAsync(action.Id, null, ct);
            await readiness.RevalidateAsync(ticket, owner, source, appId, ct, sealedVersion: true);
            await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw MiniAppReadinessService.Changed(); }
    }

    private static async Task<MiniAppReviewSummaryDto?> Replay(MiniAppReviewAuditService audit, MiniAppReviewAuditActor actor, Guid app,
        MiniAppReviewSubmitRequest request, string payload, CancellationToken ct)
    {
        var previous = await audit.FindAsync(actor, app, request.RequestId, payload, ct);
        if (previous == null) return null;
        if (previous.Action != MiniAppReviewActionKind.ReviewSubmitted || previous.MiniAppVersionId != request.VersionId) throw MiniAppReadinessService.Changed();
        try { return JsonSerializer.Deserialize<MiniAppReviewSummaryDto>(previous.ResultJson) ?? throw MiniAppReadinessService.Changed(); }
        catch (JsonException) { throw MiniAppReadinessService.Changed(); }
    }
}
