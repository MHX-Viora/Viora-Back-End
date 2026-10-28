using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppProductionLaunchService(AppDbContext db, TimeProvider clock, MiniAppProductionOptions options,
    MiniAppProductionPolicy policy, MiniAppProductionAccess access, MiniAppProductionBindingService bindings,
    IPublicWebFetcher fetcher, IDataProtectionProvider protection, MiniAppProductionAudit audit)
{
    private readonly IDataProtector receipts = protection.CreateProtector("MiniAppProduction.LaunchReceipt.v1");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<MiniAppLaunchReceiptDto> LaunchAsync(string appId, MiniAppProductionActor actor, MiniAppLaunchRequest request, CancellationToken ct)
    {
        if (!options.Enabled) throw Unavailable(); request.Validate();
        if (request.Platform != "Web") throw new DeveloperAuthException(501, "NATIVE_HOST_UNAVAILABLE", "Mini App chưa khả dụng trên thiết bị này.");
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Production launch owns its transaction.");
        var binding = bindings.RequireLaunch(request.BindingId, request.BrowserNonce);
        await access.ValidateAsync(actor, ct);
        var id = await db.MiniApps.AsNoTracking().Where(x => x.AppId == appId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct) ?? throw Unavailable();
        var before = await policy.ReadAsync(id, ct);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try { await fetcher.FetchAsync(before.Website, 256 * 1024, false, deadline.Token); }
            catch (PublicFetchException) { throw TargetUnavailable(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TargetUnavailable(); }
        }
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await policy.LockAsync(id, actor, ct);
            binding = bindings.RequireLaunch(request.BindingId, request.BrowserNonce);
            await access.ValidateAsync(actor, ct);
            var current = await policy.ReadAsync(id, ct);
            if (current.Fingerprint != before.Fingerprint) throw Changed();
            var requestHash = MiniAppReviewAuditService.Hash(new { appId, request.RequestId, request.Platform, binding.Id,
                binding.BrowserBindingHash, binding.BrowserNonceHash, actor.AccountId, actor.SourceSocialSessionId });
            var existing = await db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleOrDefaultAsync(x => x.BindingId == binding.Id && x.RequestId == request.RequestId, ct);
            if (existing != null)
            {
                if (existing.RequestHash != requestHash) throw Changed();
                if (existing.ClosedAt != null || existing.ExpiresAt <= Now || existing.PolicyFingerprint != current.Fingerprint) throw Changed();
                var saved = await db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleOrDefaultAsync(x => x.HostSessionId == existing.Id &&
                    x.BindingId == binding.Id && x.Audience == options.HostOrigin && x.ConsumedAt == null && x.ExpiresAt > Now, ct);
                if (saved?.ReceiptProtected == null) throw Changed();
                MiniAppLaunchReceiptDto receipt;
                try { receipt = JsonSerializer.Deserialize<MiniAppLaunchReceiptDto>(receipts.Unprotect(saved.ReceiptProtected)) ?? throw Changed(); }
                catch (Exception e) when (e is CryptographicException or JsonException) { throw Changed(); }
                if (receipt.SessionId != existing.Id || receipt.PublicationId != existing.PublicationId || receipt.HostOrigin != options.HostOrigin ||
                    !MiniAppProductionCredentials.EqualHash(saved.GrantHash, MiniAppProductionCredentials.Hash(receipt.Grant))) throw Changed();
                await tx.CommitAsync(ct); return receipt;
            }
            var now = Now; var since = now.AddMinutes(-1);
            if (await db.Set<MiniAppProductionHostSession>().Where(x => x.CreatedAt >= since &&
                (x.BindingId == binding.Id || actor.AccountId != null && x.AccountId == actor.AccountId)).Take(options.LaunchLimitPerMinute).CountAsync(ct) >= options.LaunchLimitPerMinute)
                throw new DeveloperAuthException(429, "HOST_LAUNCH_LIMIT", "Quá nhiều lượt mở Mini App. Vui lòng thử lại sau.");
            if (await db.Set<MiniAppProductionHostSession>().CountAsync(x => x.BindingId == binding.Id && x.ClosedAt == null && x.ExpiresAt > now &&
                (x.RuntimeCredentialHash != null || db.Set<MiniAppProductionLaunchGrant>().Any(g => g.HostSessionId == x.Id && g.ConsumedAt == null && g.ExpiresAt > now)), ct) >= options.ActiveSessionsPerBinding)
                throw new DeveloperAuthException(429, "HOST_SESSION_LIMIT", "Vui lòng đóng một Mini App trước khi mở thêm.");
            var target = current.Target;
            var session = new MiniAppProductionHostSession
            {
                MiniAppId = id, PublicationId = target.Publication.Id, MiniAppVersionId = target.Version.Id,
                ApprovedReviewRequestId = target.Publication.ApprovedReviewRequestId, SnapshotHash = target.Version.SnapshotHash,
                PublicationRevision = target.Publication.ConcurrencyToken, OperationalRevision = current.OperationalRevision,
                PolicyFingerprint = current.Fingerprint, AccountId = actor.AccountId, SourceSocialSessionId = actor.SourceSocialSessionId,
                BindingId = binding.Id, BrowserBindingHash = binding.BrowserBindingHash, BrowserNonceHash = binding.BrowserNonceHash,
                Platform = request.Platform, RequestId = request.RequestId, RequestHash = requestHash,
                ExpiresAt = Now.AddMinutes(options.SessionMinutes), LastActivityAt = Now
            };
            var raw = MiniAppProductionCredentials.New();
            var result = new MiniAppLaunchReceiptDto(session.Id, session.PublicationId, target.Version.VersionName, options.HostOrigin,
                options.HostOrigin + "/mini-app-host", session.ExpiresAt, raw, ["platform.getContext", "platform.close"], session.CorrelationId);
            db.AddRange(session, new MiniAppProductionLaunchGrant
            {
                HostSessionId = session.Id, BindingId = binding.Id, BrowserNonceHash = binding.BrowserNonceHash,
                GrantHash = MiniAppProductionCredentials.Hash(raw), Audience = options.HostOrigin,
                ExpiresAt = new[] { Now.AddSeconds(options.GrantSeconds), session.ExpiresAt, binding.ExpiresAt }.Min(),
                ReceiptProtected = receipts.Protect(JsonSerializer.Serialize(result))
            });
            audit.Append(session, MiniAppProductionEventCode.LaunchGranted);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
        }
        catch (Exception e) when (MiniAppVersionService.IsWriteConflict(e) || e is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.LockNotAvailable })
        { db.ChangeTracker.Clear(); throw Changed(); }
    }
    private static DeveloperAuthException Unavailable() => new(404, "APP_NOT_FOUND", "Mini App hiện không khả dụng.");
    private static DeveloperAuthException Changed() => new(409, "LAUNCH_CHANGED", "Phiên hoặc cấu hình đã thay đổi. Vui lòng mở lại Mini App.");
    private static DeveloperAuthException TargetUnavailable() => new(503, "TARGET_UNAVAILABLE", "Không thể kết nối Mini App lúc này. Vui lòng thử lại.");
}
