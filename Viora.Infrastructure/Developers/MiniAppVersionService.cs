using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppVersionService(AppDbContext db, MiniAppReviewAccess access, TimeProvider clock, IOptions<MiniAppReviewOptions>? options = null)
{
    public bool Enabled => options?.Value.Enabled == true;

    // Called by trusted metadata/Sandbox editors after SaveChanges, inside their owning transaction.
    public async Task SyncWorkingAsync(MiniApp app, CancellationToken ct)
    {
        if (!Enabled) return;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Working version synchronization requires an owning transaction.");
        var latest = await db.MiniAppVersions.AsNoTracking().Where(x => x.MiniAppId == app.Id).OrderByDescending(x => x.VersionCode).FirstOrDefaultAsync(ct);
        if (latest == null) return; // Existing apps enroll explicitly through POST /versions.
        var previous = MiniAppVersionSnapshotPolicy.Read(latest.SnapshotJson, latest.SnapshotHash).Snapshot;
        var (snapshot, logo) = await Capture(app, previous.Production, ct);
        MiniAppVersion version; MiniAppReviewActionKind action;
        if (latest.State == MiniAppVersionState.Draft)
        {
            if (latest.SnapshotHash == snapshot.Hash && latest.LogoUploadId == logo) return;
            var revision = Guid.NewGuid(); var now = clock.GetUtcNow().UtcDateTime;
            if (await db.MiniAppVersions.Where(x => x.Id == latest.Id && x.State == MiniAppVersionState.Draft && x.ConcurrencyToken == latest.ConcurrencyToken)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotJson, snapshot.Json).SetProperty(x => x.SnapshotHash, snapshot.Hash)
                    .SetProperty(x => x.LogoUploadId, logo).SetProperty(x => x.ConcurrencyToken, revision).SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            version = latest; version.SnapshotHash = snapshot.Hash; version.ConcurrencyToken = revision; version.UpdatedAt = now;
            action = MiniAppReviewActionKind.VersionUpdated;
        }
        else
        {
            if (latest.VersionCode == long.MaxValue) throw Conflict();
            version = new() { MiniAppId = app.Id, CreatedByDeveloperId = app.DeveloperAccountId, VersionCode = latest.VersionCode + 1,
                VersionName = latest.VersionName, SnapshotJson = snapshot.Json, SnapshotHash = snapshot.Hash, LogoUploadId = logo };
            db.MiniAppVersions.Add(version); await db.SaveChangesAsync(ct); action = MiniAppReviewActionKind.VersionCreated;
        }
        await AddAction(app.DeveloperAccountId, app.Id, version.Id, Guid.NewGuid(), snapshot.Hash, action, Summary(version), ct);
    }

    public async Task<MiniAppVersionSummaryDto> CreateAsync(Guid owner, Guid source, string appId, MiniAppVersionCreateRequest request, CancellationToken ct)
    {
        Validate(request.RequestId, request.AppVersion); var name = MiniAppInput.Text(request.VersionName, 80, true);
        await access.RequireOwnerAsync(owner, source, appId, ct);
        var hash = MiniAppReviewAuditService.Hash(new { Command = "version.create", appId, request.AppVersion, VersionName = name });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var app = await access.RequireOwnerAsync(owner, source, appId, ct);
            if (await Replay(owner, app.Id, request.RequestId, hash, ct) is { } replay) return replay;
            if (app.Version != request.AppVersion) throw Conflict();
            if (await db.MiniAppVersions.AnyAsync(x => x.MiniAppId == app.Id && x.State == MiniAppVersionState.Draft, ct))
                throw new DeveloperAuthException(409, "VERSION_DRAFT_EXISTS", "Mini App đã có bản nháp đang chỉnh sửa.");
            await LockApp(app, null, ct);
            var latest = await db.MiniAppVersions.AsNoTracking().Where(x => x.MiniAppId == app.Id).OrderByDescending(x => x.VersionCode).FirstOrDefaultAsync(ct);
            if (latest?.VersionCode == long.MaxValue) throw Conflict();
            var previous = latest == null ? null : MiniAppVersionSnapshotPolicy.Read(latest.SnapshotJson, latest.SnapshotHash).Snapshot;
            var (snapshot, logo) = await Capture(app, previous?.Production, ct);
            var version = new MiniAppVersion { MiniAppId = app.Id, CreatedByDeveloperId = owner, VersionCode = (latest?.VersionCode ?? 0) + 1,
                VersionName = name, SnapshotJson = snapshot.Json, SnapshotHash = snapshot.Hash, LogoUploadId = logo };
            db.MiniAppVersions.Add(version); await db.SaveChangesAsync(ct);
            var result = Summary(version); await AddAction(owner, app.Id, version.Id, request.RequestId, hash, MiniAppReviewActionKind.VersionCreated, result, ct);
            await access.RequireOwnerAsync(owner, source, appId, ct); await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    public async Task<MiniAppVersionSummaryDto> UpdateDraftAsync(Guid owner, Guid source, string appId, Guid versionId, MiniAppVersionUpdateRequest request, CancellationToken ct)
    {
        Validate(request.RequestId, request.ConcurrencyToken); var name = MiniAppInput.Text(request.VersionName, 80, true);
        await access.RequireOwnerAsync(owner, source, appId, ct);
        var hash = MiniAppReviewAuditService.Hash(new { Command = "version.update", appId, versionId, request.ConcurrencyToken, VersionName = name });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var app = await access.RequireOwnerAsync(owner, source, appId, ct);
            var version = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Id == versionId, ct)
                ?? throw MiniAppReviewAccess.NotFound();
            if (await Replay(owner, app.Id, request.RequestId, hash, ct) is { } replay) return replay;
            if (version.State != MiniAppVersionState.Draft || version.ConcurrencyToken != request.ConcurrencyToken) throw Conflict();
            var previous = MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot;
            await LockApp(app, null, ct);
            var (snapshot, logo) = await Capture(app, null, ct);
            var revision = Guid.NewGuid(); var now = clock.GetUtcNow().UtcDateTime;
            var changed = await db.MiniAppVersions.Where(x => x.Id == version.Id && x.State == MiniAppVersionState.Draft && x.ConcurrencyToken == request.ConcurrencyToken)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.VersionName, name).SetProperty(x => x.SnapshotJson, snapshot.Json)
                    .SetProperty(x => x.SnapshotHash, snapshot.Hash).SetProperty(x => x.LogoUploadId, logo)
                    .SetProperty(x => x.ConcurrencyToken, revision).SetProperty(x => x.UpdatedAt, now), ct);
            if (changed != 1) throw Conflict();
            version.VersionName = name; version.SnapshotHash = snapshot.Hash; version.ConcurrencyToken = revision; version.UpdatedAt = now;
            var result = Summary(version); await AddAction(owner, app.Id, version.Id, request.RequestId, hash, MiniAppReviewActionKind.VersionUpdated, result, ct);
            await access.RequireOwnerAsync(owner, source, appId, ct); await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    public async Task<MiniAppVersionDetailDto> GetAsync(Guid owner, Guid source, string appId, Guid versionId, CancellationToken ct)
    {
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var version = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId && x.MiniAppId == app.Id, ct) ?? throw MiniAppReviewAccess.NotFound();
        return new(Summary(version), MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot);
    }

    public async Task<MiniAppVersionPageDto> ListAsync(Guid owner, Guid source, string appId, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100) throw MiniAppInput.Invalid("Phân trang không hợp lệ.");
        var app = await access.RequireOwnerAsync(owner, source, appId, ct);
        var query = db.MiniAppVersions.AsNoTracking().Where(x => x.MiniAppId == app.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.VersionCode).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(Summary).ToArray(), page, pageSize, total);
    }

    private async Task LockApp(MiniApp app, bool? ssoEnabled, CancellationToken ct)
    {
        var revision = Guid.NewGuid(); var now = clock.GetUtcNow().UtcDateTime; var enabled = false;
        if (await db.MiniApps.Where(x => x.Id == app.Id && x.Version == app.Version && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, revision).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.SsoEnabled, enabled), ct) != 1) throw Conflict();
        app.Version = revision; app.SsoEnabled = enabled;
    }

    internal async Task<(CanonicalMiniAppVersionSnapshot Snapshot, Guid? Logo)> Capture(MiniApp app, MiniAppVersionSsoIntent? production, CancellationToken ct)
    {
        var snapshot = MiniAppVersionSnapshotPolicy.Create(new(1, app.Name, app.Category, app.LogoUrl, app.ShortDescription, app.Description,
            app.WebsiteUrl, null, app.PrivacyPolicyUrl, app.TermsOfServiceUrl, app.SupportEmail, false, null, null));
        var logo = app.LogoUrl == null ? null : await db.MiniAppLogoUploads.AsNoTracking()
            .Where(x => x.MiniAppId == app.Id && x.DeveloperAccountId == app.DeveloperAccountId && x.Url == app.LogoUrl)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return (snapshot, logo);
    }

    private async Task<MiniAppVersionSummaryDto?> Replay(Guid owner, Guid app, Guid requestId, string hash, CancellationToken ct)
    {
        var action = await new MiniAppReviewAuditService(db).FindAsync(new(MiniAppReviewActorType.Developer, owner), app, requestId, hash, ct);
        if (action == null) return null;
        try { return JsonSerializer.Deserialize<MiniAppVersionSummaryDto>(action.ResultJson) ?? throw Conflict(); }
        catch (JsonException) { throw Conflict(); }
    }

    private Task AddAction(Guid owner, Guid app, Guid version, Guid request, string hash, MiniAppReviewActionKind kind,
        MiniAppVersionSummaryDto result, CancellationToken ct) => new MiniAppReviewAuditService(db).AppendAsync(new()
    {
        MiniAppId = app, VersionId = version, Actor = new(MiniAppReviewActorType.Developer, owner), Action = kind,
        RequestId = request, PayloadHash = hash, CorrelationId = request, BeforeState = kind == MiniAppReviewActionKind.VersionUpdated ? "Draft" : null,
        AfterState = "Draft", ResultJson = JsonSerializer.Serialize(result)
    }, ct);
    private static MiniAppVersionSummaryDto Summary(MiniAppVersion version) => new(version.Id, version.VersionCode, version.VersionName,
        version.State.ToString(), version.SnapshotHash, version.ConcurrencyToken, version.CreatedAt, version.UpdatedAt, version.SubmittedAt);
    private static void Validate(Guid request, Guid revision) { if (request == Guid.Empty || revision == Guid.Empty) throw MiniAppInput.Invalid("Mã yêu cầu hoặc phiên bản không hợp lệ."); }
    private static DeveloperAuthException Conflict() => new(409, "VERSION_CONFLICT", "Phiên bản vừa thay đổi. Hãy tải lại trước khi tiếp tục.");
    internal static bool IsWriteConflict(Exception error) => error is DbUpdateConcurrencyException ||
        error is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } ||
        (error is InvalidOperationException or DbUpdateException && error.InnerException is { } inner && IsWriteConflict(inner));
}
