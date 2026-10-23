using System.Data;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppProductionIssuedRuntime(MiniAppProductionRedeemedDto Public, [property: JsonIgnore] string Credential)
{
    public override string ToString() => $"Issued Mini App runtime {Public.SessionId}; credential redacted";
}

public sealed class MiniAppProductionGrantService(AppDbContext db, TimeProvider clock, MiniAppProductionOptions options,
    MiniAppProductionPolicy policy, MiniAppProductionAccess access, MiniAppProductionBindingService bindings, MiniAppProductionAudit audit)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<MiniAppProductionIssuedRuntime> RedeemAsync(string? raw, string? nonce, string? browserCookie, CancellationToken ct)
    {
        if (!options.Enabled || !MiniAppProductionCredentials.Valid(raw) || !MiniAppProductionCredentials.Valid(nonce)) throw Denied();
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Redemption owns its transaction.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = MiniAppProductionCredentials.Hash(raw!);
            var grant = await db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleOrDefaultAsync(x => x.GrantHash == hash, ct);
            if (grant == null || grant.ConsumedAt != null || grant.ExpiresAt <= Now || grant.Audience != options.HostOrigin) throw Denied();
            var session = await db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == grant.HostSessionId && x.BindingId == grant.BindingId, ct);
            var nonceHash = MiniAppProductionCredentials.Hash(nonce!);
            if (session == null || session.ClosedAt != null || session.ExpiresAt <= Now || session.RuntimeCredentialHash != null ||
                !MiniAppProductionCredentials.EqualHash(grant.BrowserNonceHash, nonceHash) || !MiniAppProductionCredentials.EqualHash(session.BrowserNonceHash, nonceHash)) throw Denied();
            bindings.RequireBrowser(new(session.BindingId, session.BrowserBindingHash, session.BrowserNonceHash, session.ExpiresAt), browserCookie);
            var actor = new MiniAppProductionActor(session.AccountId, session.SourceSocialSessionId);
            await policy.LockAsync(session.MiniAppId, actor, ct); await access.ValidateAsync(actor, ct);
            var current = await policy.RequirePinnedAsync(session, ct);
            var credential = MiniAppProductionCredentials.New(); var version = Guid.NewGuid(); var now = Now;
            var consumed = await db.Set<MiniAppProductionLaunchGrant>().Where(x => x.Id == grant.Id && x.Version == grant.Version && x.ConsumedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now).SetProperty(x => x.ReceiptProtected, (string?)null).SetProperty(x => x.Version, version), ct);
            if (consumed != 1) throw Denied();
            var runtimeHash = MiniAppProductionCredentials.Hash(credential);
            var issued = await db.Set<MiniAppProductionHostSession>().Where(x => x.Id == session.Id && x.Version == session.Version &&
                x.ClosedAt == null && x.ExpiresAt > now && x.RuntimeCredentialHash == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RuntimeCredentialHash, runtimeHash).SetProperty(x => x.Version, version), ct);
            if (issued != 1) throw Denied();
            audit.Append(session, MiniAppProductionEventCode.Redeemed);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            var snapshot = current.Target.Snapshot; var description = snapshot.ShortDescription ?? snapshot.Description;
            var item = new MiniAppPublicItemDto(current.AppId, snapshot.Name, snapshot.LogoUrl,
                description == null ? null : string.Concat(description.EnumerateRunes().Take(300).Select(x => x.ToString())), snapshot.Category, current.Target.Version.VersionName);
            return new(new(session.Id, DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc), item, current.Website.AbsoluteUri, current.Website.GetLeftPart(UriPartial.Authority),
                new(current.AppId), ["platform.getContext", "platform.close"], options.HeartbeatSeconds), credential);
        }
        catch (Exception e) when (MiniAppVersionService.IsWriteConflict(e) || e is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.LockNotAvailable })
        { db.ChangeTracker.Clear(); throw Denied(); }
    }
    private static DeveloperAuthException Denied() => new(403, "HOST_REDEEM_DENIED", "Không thể xác minh lượt mở Mini App. Vui lòng mở lại.");
}
