using System.Data;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppReviewDecisionService(AppDbContext db, MiniAppReviewAccess access, TimeProvider clock, MiniAppReadinessService? readiness = null)
{
    private sealed record ApprovalTarget(Guid VersionId, string SnapshotHash, string EvidenceHash, string SafetyFingerprint);
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "name", "category", "logoUrl", "shortDescription", "description", "websiteUrl",
        "domain", "privacyPolicyUrl", "termsOfServiceUrl", "supportEmail", "ssoEnabled", "sandbox.clientType", "sandbox.scopes", "sandbox.redirectUris",
        "production.clientType", "production.scopes", "production.redirectUris" };
    public Task<MiniAppReviewSummaryDto> RejectAsync(Guid admin, Guid source, Guid reviewId, MiniAppReviewDecisionRequest request, CancellationToken ct) =>
        DecideCore(admin, source, reviewId, request, MiniAppReviewStatus.Rejected, ct);
    public Task<MiniAppReviewSummaryDto> RequestChangesAsync(Guid admin, Guid source, Guid reviewId, MiniAppReviewDecisionRequest request, CancellationToken ct) =>
        DecideCore(admin, source, reviewId, request, MiniAppReviewStatus.ChangesRequested, ct);
    public Task<MiniAppReviewSummaryDto> ApproveAsync(Guid admin, Guid source, Guid reviewId, MiniAppReviewDecisionRequest request, CancellationToken ct) =>
        DecideCore(admin, source, reviewId, request, MiniAppReviewStatus.Approved, ct);

    private async Task<MiniAppReviewSummaryDto> DecideCore(Guid admin, Guid source, Guid reviewId, MiniAppReviewDecisionRequest request, MiniAppReviewStatus decision, CancellationToken ct)
    {
        Validate(request.RequestId, request.ConcurrencyToken); var reason = Reason(request.Reason, decision != MiniAppReviewStatus.Approved);
        if (decision == MiniAppReviewStatus.ChangesRequested ? request.Changes?.Count is not (>= 1 and <= 20) : request.Changes?.Count > 0)
            throw MiniAppInput.Invalid("Yêu cầu chỉnh sửa phải có từ 1 đến 20 mục hợp lệ.");
        var changes = request.Changes?.Select(item => item == null || !Fields.Contains(item.Field)
            ? throw MiniAppInput.Invalid("Trường cần chỉnh sửa không hợp lệ.") : new MiniAppReviewChangeItem(item.Field, MiniAppInput.Text(item.Message, 2000, true))).ToArray() ?? [];
        var json = JsonSerializer.Serialize(changes); if (Encoding.UTF8.GetByteCount(json) > 16384) throw MiniAppInput.Invalid("Danh sách chỉnh sửa quá lớn.");
        await access.RequireAdminAsync(admin, source, ct);
        var kind = decision switch { MiniAppReviewStatus.Approved => MiniAppReviewActionKind.ReviewApproved,
            MiniAppReviewStatus.Rejected => MiniAppReviewActionKind.ReviewRejected, _ => MiniAppReviewActionKind.ChangesRequested };
        var payload = MiniAppReviewAuditService.Hash(new { Command = kind.ToString(), reviewId, request.ConcurrencyToken, reason, changes });
        ApprovalTarget? approval = null;
        if (decision == MiniAppReviewStatus.Approved)
        {
            var preflight = await Review(reviewId, ct);
            if (await Replay(new(db), new(MiniAppReviewActorType.Admin, admin), preflight, request.RequestId, payload, ct) is { } replay) return replay;
            if (preflight.Status != MiniAppReviewStatus.InReview || preflight.ConcurrencyToken != request.ConcurrencyToken) throw Conflict();
            if (preflight.AssignedAdminAccountId != admin) throw MiniAppReviewAccess.Forbidden();
            approval = await Approval(preflight, true, ct);
        }
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await access.RequireAdminAsync(admin, source, ct); var review = await Review(reviewId, ct);
            var audit = new MiniAppReviewAuditService(db); var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Admin, admin);
            if (await Replay(audit, actor, review, request.RequestId, payload, ct) is { } replay) return replay;
            if (review.Status != MiniAppReviewStatus.InReview || review.ConcurrencyToken != request.ConcurrencyToken) throw Conflict();
            if (review.AssignedAdminAccountId != admin) throw MiniAppReviewAccess.Forbidden();
            if (approval != null && await Approval(review, false, ct) != approval) throw Unsafe();
            var now = clock.GetUtcNow().UtcDateTime; var revision = Guid.NewGuid();
            if (await db.MiniAppReviewRequests.Where(x => x.Id == reviewId && x.Status == MiniAppReviewStatus.InReview &&
                x.AssignedAdminAccountId == admin && x.ConcurrencyToken == request.ConcurrencyToken).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, decision)
                    .SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionReason, reason).SetProperty(x => x.ChangeRequestsJson, json)
                    .SetProperty(x => x.ConcurrencyToken, revision), ct) != 1) throw Conflict();
            review.Status = decision; review.DecidedAt = now; review.DecisionReason = reason; review.ChangeRequestsJson = json; review.ConcurrencyToken = revision;
            var versionCode = await db.MiniAppVersions.Where(x => x.Id == review.MiniAppVersionId).Select(x => x.VersionCode).SingleAsync(ct);
            var result = MiniAppReviewService.Summary(review, versionCode);
            var action = await audit.AppendAsync(new() { MiniAppId = review.MiniAppId, VersionId = review.MiniAppVersionId, ReviewId = review.Id,
                Actor = actor, Action = kind, RequestId = request.RequestId, CorrelationId = request.RequestId, PayloadHash = payload,
                BeforeState = "InReview", AfterState = decision.ToString(), Reason = reason, ResultJson = JsonSerializer.Serialize(result) }, ct);
            await new MiniAppReviewNotificationService(db, clock).QueueAsync(action.Id, null, ct);
            await access.RequireAdminAsync(admin, source, ct);
            if (approval != null && await Approval(review, false, ct) != approval) throw Unsafe();
            await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    private async Task<ApprovalTarget> Approval(MiniAppReviewRequest review, bool probe, CancellationToken ct)
    {
        if (readiness == null) throw new DeveloperAuthException(503, "REVIEW_CHECKS_UNAVAILABLE", "Dịch vụ kiểm tra xét duyệt chưa sẵn sàng.");
        var version = await db.MiniAppVersions.AsNoTracking().SingleAsync(x => x.Id == review.MiniAppVersionId && x.MiniAppId == review.MiniAppId, ct);
        MiniAppReviewEvidencePolicy.Read(review, version);
        var target = await readiness.TargetAsync(version, probe, ct);
        if (target.BlockingReasons.Length != 0) throw Unsafe();
        return new(version.Id, version.SnapshotHash, MiniAppReviewAuditService.Hash(review.EvidenceAttestationJson), target.Fingerprint);
    }
    private static DeveloperAuthException Unsafe() => new(409, "REVIEW_TARGET_UNSAFE", "Phiên bản đích không còn đáp ứng điều kiện an toàn. Hãy kiểm tra lại.");

    public Task<MiniAppReviewSummaryDto> StartAsync(Guid admin, Guid source, Guid reviewId, MiniAppReviewCommand request, CancellationToken ct) =>
        AssignCore(admin, source, reviewId, request.RequestId, request.ConcurrencyToken, admin, null, true, ct);
    public Task<MiniAppReviewSummaryDto> AssignAsync(Guid admin, Guid source, Guid reviewId, MiniAppReviewAssignmentRequest request, CancellationToken ct) =>
        AssignCore(admin, source, reviewId, request.RequestId, request.ConcurrencyToken, request.AdminAccountId, Reason(request.Reason, true), false, ct);

    private async Task<MiniAppReviewSummaryDto> AssignCore(Guid admin, Guid source, Guid reviewId, Guid request, Guid expected, Guid assignee, string? reason, bool start, CancellationToken ct)
    {
        Validate(request, expected); if (assignee == Guid.Empty) throw MiniAppInput.Invalid("Admin được giao không hợp lệ.");
        await access.RequireAdminAsync(admin, source, ct);
        var kind = start ? MiniAppReviewActionKind.ReviewStarted : MiniAppReviewActionKind.ReviewAssigned;
        var payload = MiniAppReviewAuditService.Hash(new { Command = kind.ToString(), reviewId, expected, assignee, reason });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await access.RequireAdminAsync(admin, source, ct); var review = await Review(reviewId, ct);
            var audit = new MiniAppReviewAuditService(db); var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Admin, admin);
            if (await Replay(audit, actor, review, request, payload, ct) is { } replay) return replay;
            if (review.ConcurrencyToken != expected || review.Status != (start ? MiniAppReviewStatus.PendingReview : MiniAppReviewStatus.InReview)) throw Conflict();
            if (!await db.Accounts.AnyAsync(x => x.Id == assignee && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active && x.DeletedAt == null, ct))
                throw MiniAppReviewAccess.Forbidden();
            var now = clock.GetUtcNow().UtcDateTime; var revision = Guid.NewGuid(); var startedAt = start ? now : review.ReviewStartedAt;
            if (await db.MiniAppReviewRequests.Where(x => x.Id == reviewId && x.ConcurrencyToken == expected && x.Status == review.Status)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppReviewStatus.InReview).SetProperty(x => x.AssignedAdminAccountId, (Guid?)assignee)
                    .SetProperty(x => x.ReviewStartedAt, startedAt).SetProperty(x => x.ConcurrencyToken, revision), ct) != 1) throw Conflict();
            var before = review.Status.ToString(); var previousAssignee = review.AssignedAdminAccountId;
            review.Status = MiniAppReviewStatus.InReview; review.AssignedAdminAccountId = assignee; review.ReviewStartedAt = startedAt; review.ConcurrencyToken = revision;
            var versionCode = await db.MiniAppVersions.Where(x => x.Id == review.MiniAppVersionId).Select(x => x.VersionCode).SingleAsync(ct);
            var result = MiniAppReviewService.Summary(review, versionCode);
            var action = await audit.AppendAsync(new() { MiniAppId = review.MiniAppId, VersionId = review.MiniAppVersionId, ReviewId = review.Id,
                Actor = actor, Action = kind, RequestId = request, CorrelationId = request, PayloadHash = payload, BeforeState = before, AfterState = "InReview", Reason = reason,
                MetadataJson = JsonSerializer.Serialize(new { assignedAdminAccountId = assignee, previousAssignee }), ResultJson = JsonSerializer.Serialize(result) }, ct);
            if (start) await new MiniAppReviewNotificationService(db, clock).QueueAsync(action.Id, null, ct);
            await access.RequireAdminAsync(admin, source, ct); await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    private async Task<MiniAppReviewRequest> Review(Guid id, CancellationToken ct)
    {
        var review = await db.MiniAppReviewRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            db.MiniApps.Any(a => a.Id == x.MiniAppId && a.DeletedAt == null), ct) ?? throw MiniAppReviewAccess.NotFound();
        if (await db.MiniAppOperationalStates.AnyAsync(x => x.MiniAppId == review.MiniAppId && x.Status == MiniAppOperationalStatus.Suspended, ct)) throw MiniAppReviewAccess.Forbidden();
        return review;
    }
    private static async Task<MiniAppReviewSummaryDto?> Replay(MiniAppReviewAuditService audit, MiniAppReviewAuditActor actor,
        MiniAppReviewRequest review, Guid request, string payload, CancellationToken ct)
    {
        var previous = await audit.FindAsync(actor, review.MiniAppId, request, payload, ct);
        if (previous == null) return null;
        if (previous.ReviewRequestId != review.Id) throw Conflict();
        try { return JsonSerializer.Deserialize<MiniAppReviewSummaryDto>(previous.ResultJson) ?? throw Conflict(); }
        catch (JsonException) { throw Conflict(); }
    }
    private static void Validate(Guid request, Guid revision) { if (request == Guid.Empty || revision == Guid.Empty) throw MiniAppInput.Invalid("Mã yêu cầu và phiên bản xét duyệt không hợp lệ."); }
    private static string? Reason(string? value, bool required)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : MiniAppInput.Text(value, 2000, true);
        if (required && text?.Length is not >= 10) throw MiniAppInput.Invalid("Lý do phải có từ 10 đến 2000 ký tự."); return text;
    }
    private static DeveloperAuthException Conflict() => new(409, "REVIEW_CONFLICT", "Phiên xét duyệt đã thay đổi. Hãy tải lại.");
}
