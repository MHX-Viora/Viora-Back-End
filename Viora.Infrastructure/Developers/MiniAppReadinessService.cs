using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppReviewReadinessTicket(MiniAppReviewReadinessDto Readiness, string SafetyFingerprint, int EvidenceSchemaVersion = 1);
public sealed record MiniAppTargetSafety(MiniAppVersionSnapshot Snapshot, string Fingerprint, string[] BlockingReasons);
public sealed record MiniAppPublicationReadinessTicket(MiniAppPublicationReadinessDto Readiness, string Fingerprint);

public sealed class MiniAppReadinessService(AppDbContext db, MiniAppReviewAccess access, MiniAppVersionService versions,
    MiniAppSandboxConfigurationService configuration, MiniAppSandboxEvidenceReader evidence, IPublicWebFetcher fetcher, TimeProvider clock,
    MiniAppBridgeReviewEvidence? bridgeEvidence = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static readonly SandboxCheckKey[] Observations = [SandboxCheckKey.Runtime, SandboxCheckKey.Bridge, SandboxCheckKey.Consent, SandboxCheckKey.Token, SandboxCheckKey.UserInfo];

    public async Task<MiniAppPublicationReadinessTicket> PublicationAsync(Guid owner, Guid source, string appId, Guid versionId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Publication network preflight cannot join a business transaction.");
        if (versionId == Guid.Empty) throw MiniAppInput.Invalid("Phải chọn phiên bản đã duyệt.");
        var ticket = await PublicationTargetAsync(owner, source, appId, versionId, true, ct);
        var current = await PublicationTargetAsync(owner, source, appId, versionId, false, ct);
        if (ticket.Fingerprint != current.Fingerprint || ticket.Readiness.CanPublish && !current.Readiness.CanPublish) throw Changed();
        return ticket;
    }

    // Database-only validation for the publication command's transaction.
    public async Task RevalidatePublicationAsync(MiniAppPublicationReadinessTicket ticket, Guid owner, Guid source, string appId, CancellationToken ct)
    {
        if (!ticket.Readiness.CanPublish) throw Blocked();
        var current = await PublicationTargetAsync(owner, source, appId, ticket.Readiness.VersionId, false, ct);
        if (!current.Readiness.CanPublish || current.Fingerprint != ticket.Fingerprint) throw Changed();
    }

    private async Task<MiniAppPublicationReadinessTicket> PublicationTargetAsync(Guid owner, Guid source, string appId, Guid versionId, bool probe, CancellationToken ct)
    {
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var version = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId && x.MiniAppId == app.Id, ct)
            ?? throw MiniAppReviewAccess.NotFound();
        var review = await db.MiniAppReviewRequests.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.MiniAppVersionId == versionId &&
            x.Status == MiniAppReviewStatus.Approved, ct);
        var approved = review != null && review.DecidedAt != null && review.DeveloperAccountId == owner && version.State != MiniAppVersionState.Draft;
        if (approved) MiniAppReviewEvidencePolicy.Read(review!, version);
        var publication = await db.MiniAppPublications.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Status == MiniAppPublicationStatus.Published, ct);
        var state = await db.MiniAppOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id, ct);
        var safety = await TargetAsync(version, probe && approved, ct);
        var blockers = safety.BlockingReasons.ToList();
        if (!approved) blockers.Add("TARGET_NOT_APPROVED");
        if (publication?.MiniAppVersionId == versionId) blockers.Add("TARGET_ALREADY_PUBLISHED");
        var fingerprint = MiniAppReviewAuditService.Hash(new { safety.Fingerprint, app.Version, ReviewId = review?.Id,
            ReviewRevision = review?.ConcurrencyToken, Evidence = review?.EvidenceAttestationJson, PublicationId = publication?.Id,
            PublicationRevision = publication?.ConcurrencyToken, OperationalRevision = state?.ConcurrencyToken });
        return new(new(versionId, version.SnapshotHash, approved ? review!.Id : null, app.Version, publication?.Id, publication?.ConcurrencyToken,
            state?.ConcurrencyToken, blockers.Count == 0, publication != null && !safety.BlockingReasons.Contains("APP_SUSPENDED"), Now, blockers.ToArray()), fingerprint);
    }

    public async Task<MiniAppReviewReadinessTicket> PrepareAsync(Guid owner, Guid source, string appId, Guid? versionId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Readiness network preflight cannot join a business transaction.");
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var version = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id &&
            (versionId == null ? x.State == MiniAppVersionState.Draft : x.Id == versionId), ct) ?? throw MiniAppReviewAccess.NotFound();
        if (version.State != MiniAppVersionState.Draft) throw Changed();
        var safety = await TargetAsync(version, false, ct);
        var working = await versions.Capture(app, safety.Snapshot.Production, ct);
        var blockers = new List<string>(safety.BlockingReasons);
        if (working.Snapshot.Hash != version.SnapshotHash || working.Logo != version.LogoUploadId) blockers.Add("VERSION_WORKING_CHANGED");
        if (await db.MiniAppReviewRequests.AnyAsync(x => x.MiniAppId == app.Id && (x.Status == MiniAppReviewStatus.PendingReview || x.Status == MiniAppReviewStatus.InReview), ct)) blockers.Add("REVIEW_ALREADY_ACTIVE");
        if (bridgeEvidence != null)
        {
            var probed = await TargetAsync(version, true, ct);
            blockers.AddRange(probed.BlockingReasons);
            var session = await bridgeEvidence.ReadAsync(app, owner, source, null, ct);
            if (session == null) blockers.Add("BRIDGE_FRESH_SOURCE_REQUIRED");
            var checks = MiniAppBridgeReviewEvidence.Checks(safety.Snapshot, session, probed.BlockingReasons.Length == 0, Now);
            var ready = new MiniAppReviewReadinessDto(version.Id, version.SnapshotHash, version.ConcurrencyToken, app.Version,
                blockers.Count == 0 && checks.All(x => !x.Applicable || x.Status == "Passed"), Now, session?.Id,
                MiniAppBridgeReviewEvidence.Fingerprint(app, version), checks, blockers.Distinct(StringComparer.Ordinal).ToArray(), []);
            var bridgeTicket = new MiniAppReviewReadinessTicket(ready, safety.Fingerprint, 2);
            if (ready.ReadyForReview) await RevalidateAsync(bridgeTicket, owner, source, appId, ct);
            return bridgeTicket;
        }
        SandboxReadinessDto sandbox;
        try { sandbox = await configuration.ReadinessAsync(new(SandboxActorKind.Developer, owner, source), appId, ct); }
        catch (DeveloperAuthException error) when (error.Code is "SANDBOX_DISABLED" or "SANDBOX_APP_UNAVAILABLE")
        {
            var checks = Enum.GetValues<SandboxCheckKey>().Select(key => new SandboxReadinessCheck(
                char.ToLowerInvariant(key.ToString()[0]) + key.ToString()[1..], true, "NotImplemented", "missing_evidence", "Server", null)).ToArray();
            sandbox = new("Sandbox", "", false, false, false, Now, checks, [error.Code], []);
        }
        blockers.AddRange(sandbox.BlockingIssues.Select(x => "SANDBOX_" + x));
        var runId = await evidence.LatestRunIdAsync(app.Id, sandbox.ConfigurationFingerprint, ct);
        if (runId == null || !await OwnRunAsync(runId.Value, app.Id, owner, source, sandbox.ConfigurationFingerprint, ct)) blockers.Add("SANDBOX_FRESH_SOURCE_REQUIRED");
        var currentApp = await access.RequireOwnerAsync(owner, source, appId, ct);
        var current = await db.MiniAppVersions.AsNoTracking().SingleAsync(x => x.Id == version.Id, ct);
        var currentSafety = await TargetAsync(current, false, ct);
        if (currentApp.Version != app.Version || current.ConcurrencyToken != version.ConcurrencyToken || current.State != MiniAppVersionState.Draft ||
            current.SnapshotHash != version.SnapshotHash || currentSafety.Fingerprint != safety.Fingerprint) throw Changed();
        var dto = new MiniAppReviewReadinessDto(version.Id, version.SnapshotHash, version.ConcurrencyToken, app.Version,
            sandbox.ReadyForReview && blockers.Count == 0, Now, runId, sandbox.ConfigurationFingerprint, sandbox.Checks,
            blockers.Distinct(StringComparer.Ordinal).ToArray(), sandbox.Warnings);
        var ticket = new MiniAppReviewReadinessTicket(dto, safety.Fingerprint);
        if (dto.ReadyForReview) await RevalidateAsync(ticket, owner, source, appId, ct);
        return ticket;
    }

    // Database-only recheck, including the original run's provenance. Safe to use immediately before commit.
    public async Task RevalidateAsync(MiniAppReviewReadinessTicket ticket, Guid owner, Guid source, string appId, CancellationToken ct, bool sealedVersion = false)
    {
        var ready = ticket.Readiness;
        if (!ready.ReadyForReview || ready.SandboxRunId == null || ready.ConfigurationFingerprint == null) throw Blocked();
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var version = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ready.VersionId && x.MiniAppId == app.Id, ct) ?? throw MiniAppReviewAccess.NotFound();
        if (app.Version != ready.AppVersion || version.SnapshotHash != ready.SnapshotHash ||
            (sealedVersion ? version.State != MiniAppVersionState.Submitted : version.State != MiniAppVersionState.Draft || version.ConcurrencyToken != ready.ConcurrencyToken)) throw Changed();
        var safety = await TargetAsync(version, false, ct); var working = await versions.Capture(app, safety.Snapshot.Production, ct);
        if (safety.BlockingReasons.Length != 0 || safety.Fingerprint != ticket.SafetyFingerprint || working.Snapshot.Hash != ready.SnapshotHash || working.Logo != version.LogoUploadId) throw Changed();
        if (ticket.EvidenceSchemaVersion == 2)
        {
            if (bridgeEvidence == null || ready.ConfigurationFingerprint != MiniAppBridgeReviewEvidence.Fingerprint(app, version)) throw Changed();
            var session = await bridgeEvidence.ReadAsync(app, owner, source, ready.SandboxRunId, ct);
            if (session == null || ready.RequiredChecks.Where(x => x.EvidenceSource == "ServerProtocol").Any(x => x.EvidenceAt != session.ConsumedAt)) throw Changed();
            return;
        }
        if (await evidence.LatestRunIdAsync(app.Id, ready.ConfigurationFingerprint, ct) != ready.SandboxRunId ||
            !await OwnRunAsync(ready.SandboxRunId.Value, app.Id, owner, source, ready.ConfigurationFingerprint, ct) ||
            !await evidence.IsFreshAsync(ready.SandboxRunId.Value, ready.ConfigurationFingerprint, ct)) throw Changed();
        foreach (var key in Observations)
        {
            var wire = char.ToLowerInvariant(key.ToString()[0]) + key.ToString()[1..];
            var original = ready.RequiredChecks.Single(x => x.Key == wire);
            if (!original.Applicable) continue;
            var observed = await evidence.LatestAsync(app.Id, ready.ConfigurationFingerprint, key, ct, ready.SandboxRunId);
            var requiredSource = key is SandboxCheckKey.Runtime or SandboxCheckKey.Bridge ? SandboxEvidenceSource.TrustedBrowser : SandboxEvidenceSource.ServerProtocol;
            if (observed.Status != SandboxCheckStatus.Passed || observed.EvidenceSource != requiredSource || observed.EvidenceAt == null ||
                observed.EvidenceAt < Now.AddMinutes(-5) || observed.EvidenceAt > Now.AddSeconds(1) || observed.EvidenceAt != original.EvidenceAt) throw Changed();
        }
    }

    private Task<bool> OwnRunAsync(Guid run, Guid app, Guid owner, Guid source, string fingerprint, CancellationToken ct)
    {
        var now = Now;
        return (from r in db.MiniAppSandboxTestRuns.AsNoTracking()
            join session in db.MiniAppSandboxSessions.AsNoTracking() on r.SandboxSessionId equals session.Id
            where r.Id == run && r.Status == SandboxTestRunStatus.Completed && r.ConfigurationFingerprint == fingerprint
                && r.CompletedAt >= now.AddMinutes(-5) && r.CompletedAt <= now.AddSeconds(1) && session.MiniAppId == app
                && session.ActorDeveloperAccountId == owner && session.SourceDeveloperSessionId == source && session.RevokedAt == null && session.ExpiresAt > now
            select r.Id).AnyAsync(ct);
    }

    // Validates the exact sealed target independently of the latest working draft or its source session.
    public async Task<MiniAppTargetSafety> TargetAsync(MiniAppVersion version, bool probe, CancellationToken ct)
    {
        if (probe && db.Database.CurrentTransaction != null) throw new InvalidOperationException("Target network preflight cannot join a business transaction.");
        if (version.SnapshotSchemaVersion != 1) throw Changed();
        var snapshot = MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot;
        var app = await db.MiniApps.AsNoTracking().Include(x => x.Developer).SingleOrDefaultAsync(x => x.Id == version.MiniAppId && x.DeletedAt == null, ct) ?? throw MiniAppReviewAccess.NotFound();
        var state = await db.MiniAppOperationalStates.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id, ct);
        var proof = await db.MiniAppDomainVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Hostname == snapshot.Domain && x.RevokedAt == null, ct);
        var blockers = new List<string>();
        if (app.Developer.Status != DeveloperStatus.Active || app.Developer.LockedUntil > Now) blockers.Add("DEVELOPER_UNAVAILABLE");
        if (state?.Status == MiniAppOperationalStatus.Suspended || state == null && app.Status == MiniAppStatus.Suspended) blockers.Add("APP_SUSPENDED");
        if (new[] { snapshot.LogoUrl, snapshot.ShortDescription, snapshot.Description, snapshot.WebsiteUrl, snapshot.PrivacyPolicyUrl, snapshot.TermsOfServiceUrl, snapshot.SupportEmail }.Any(string.IsNullOrWhiteSpace)) blockers.Add("METADATA_INCOMPLETE");
        if (proof is not { Status: MiniAppDomainStatus.Verified } || proof.ExpiresAt <= Now) blockers.Add("DOMAIN_UNVERIFIED");
        if (version.LogoUploadId is { } logo && !await db.MiniAppLogoUploads.AnyAsync(x => x.Id == logo && x.DeveloperAccountId == app.DeveloperAccountId && x.Url == snapshot.LogoUrl, ct)) blockers.Add("VERSION_LOGO_UNAVAILABLE");
        if (probe)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                foreach (var url in new[] { snapshot.WebsiteUrl, snapshot.PrivacyPolicyUrl, snapshot.TermsOfServiceUrl }.Where(x => x != null))
                    await fetcher.FetchAsync(new Uri(url!), 256 * 1024, false, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { blockers.Add("TARGET_CHECK_TIMEOUT"); }
            catch (PublicFetchException) { blockers.Add("TARGET_PUBLIC_PAGE_UNAVAILABLE"); }
        }
        var fingerprint = MiniAppReviewAuditService.Hash(new { version.SnapshotHash, app.DeveloperAccountId, app.Developer.SecurityVersion, app.Developer.Status,
            app.Developer.LockedUntil, OperationalRevision = state?.ConcurrencyToken, OperationalStatus = state?.Status, ProofId = proof?.Id, ProofRevision = proof?.Version, proof?.ExpiresAt });
        return new(snapshot, fingerprint, blockers.ToArray());
    }
    public static DeveloperAuthException Changed() => new(409, "REVIEW_READINESS_CHANGED", "Cấu hình hoặc bằng chứng đã thay đổi. Hãy kiểm tra readiness lại.");
    public static DeveloperAuthException Blocked() => new(409, "REVIEW_NOT_READY", "Mini App chưa đủ điều kiện gửi xét duyệt.");
}
