using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppVersionCredentials(AppDbContext db, MiniAppReviewAccess access, MiniAppSecretProtector protector, TimeProvider clock)
{
    private sealed record Prepared(Guid VersionId, Guid Revision, Guid ClientIdentity, string ClientId, Guid SecretId);

    public async Task<MiniAppVersionCredentialDto> PrepareAsync(Guid owner, Guid source, string appId, Guid versionId,
        MiniAppVersionCommand request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.ConcurrencyToken == Guid.Empty) throw MiniAppInput.Invalid("Mã yêu cầu hoặc phiên bản không hợp lệ.");
        await access.RequireOwnerAsync(owner, source, appId, ct);
        var payload = MiniAppReviewAuditService.Hash(new { Command = "version.credential.prepare", appId, versionId, request.ConcurrencyToken });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var app = await access.RequireOwnerAsync(owner, source, appId, ct);
            var version = await db.MiniAppVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId && x.MiniAppId == app.Id, ct)
                ?? throw MiniAppReviewAccess.NotFound();
            var audit = new MiniAppReviewAuditService(db);
            var actor = new MiniAppReviewAuditActor(MiniAppReviewActorType.Developer, owner);
            var previous = await audit.FindAsync(actor, app.Id, request.RequestId, payload, ct);
            if (previous != null)
            {
                if (previous.MiniAppId != app.Id || previous.MiniAppVersionId != versionId || previous.PayloadHash != payload
                    || previous.Action != MiniAppReviewActionKind.CredentialPrepared) throw Conflict();
                Prepared replay;
                try { replay = JsonSerializer.Deserialize<Prepared>(previous.ResultJson) ?? throw Conflict(); }
                catch (JsonException) { throw Conflict(); }
                var replayed = await Reveal(replay, app.Id, ct);
                await access.RequireOwnerAsync(owner, source, appId, ct); return replayed;
            }
            if (version.State != MiniAppVersionState.Draft || version.ConcurrencyToken != request.ConcurrencyToken) throw Conflict();
            var snapshot = MiniAppVersionSnapshotPolicy.Read(version.SnapshotJson, version.SnapshotHash).Snapshot;
            if (!snapshot.SsoEnabled || snapshot.Production is not { ClientType: "Confidential" } intent)
                throw new DeveloperAuthException(409, "CREDENTIAL_PREPARATION_INAPPLICABLE", "Chỉ bản nháp dùng Confidential SSO mới cần chuẩn bị credential.");
            var now = clock.GetUtcNow().UtcDateTime; var appRevision = Guid.NewGuid();
            if (await db.MiniApps.Where(x => x.Id == app.Id && x.Version == app.Version && x.DeletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, appRevision).SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            var client = await db.MiniAppOAuthClients.AsNoTracking().SingleOrDefaultAsync(x => x.MiniAppId == app.Id && x.Environment == MiniAppOAuthEnvironment.Production, ct);
            if (client?.RevokedAt != null || intent.ClientIdentity != null && (client?.Id != intent.ClientIdentity || client.ClientKey != intent.ClientId)) throw Conflict();
            if (client == null)
            {
                client = new() { MiniAppId = app.Id, Environment = MiniAppOAuthEnvironment.Production, ClientType = "Confidential",
                    ClientKey = "mc_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), AllowedScopes = "openid" };
                db.MiniAppOAuthClients.Add(client); await db.SaveChangesAsync(ct);
            }
            var secret = await db.MiniAppClientSecrets.AsNoTracking().SingleOrDefaultAsync(x => x.OAuthClientId == client.Id
                && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), ct);
            if (secret == null)
            {
                // Expired credentials cannot serve runtime; an active credential is always reused.
                await db.MiniAppClientSecrets.Where(x => x.OAuthClientId == client.Id && x.RevokedAt == null && x.ExpiresAt <= now)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
                var encrypted = protector.Encrypt("cs_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)), client.Id);
                secret = new() { OAuthClientId = client.Id, SecretEncrypted = encrypted.Ciphertext, SecretKeyVersion = encrypted.Version };
                db.MiniAppClientSecrets.Add(secret); await db.SaveChangesAsync(ct);
            }
            var canonical = MiniAppVersionSnapshotPolicy.Create(snapshot with { Production = intent with { ClientIdentity = client.Id, ClientId = client.ClientKey } });
            var revision = Guid.NewGuid();
            if (await db.MiniAppVersions.Where(x => x.Id == versionId && x.State == MiniAppVersionState.Draft && x.ConcurrencyToken == request.ConcurrencyToken)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotJson, canonical.Json).SetProperty(x => x.SnapshotHash, canonical.Hash)
                    .SetProperty(x => x.ConcurrencyToken, revision).SetProperty(x => x.UpdatedAt, now), ct) != 1) throw Conflict();
            var prepared = new Prepared(versionId, revision, client.Id, client.ClientKey, secret.Id);
            await audit.AppendAsync(new() { MiniAppId = app.Id, VersionId = versionId, Actor = actor,
                Action = MiniAppReviewActionKind.CredentialPrepared, RequestId = request.RequestId, CorrelationId = request.RequestId,
                PayloadHash = payload, ResultJson = JsonSerializer.Serialize(prepared), BeforeState = "Draft", AfterState = "Draft" }, ct);
            var result = await Reveal(prepared, app.Id, ct);
            await access.RequireOwnerAsync(owner, source, appId, ct); await tx.CommitAsync(ct); return result;
        }
        catch (Exception error) when (MiniAppVersionService.IsWriteConflict(error)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    private async Task<MiniAppVersionCredentialDto> Reveal(Prepared prepared, Guid app, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var secret = await (from credential in db.MiniAppClientSecrets.AsNoTracking()
            join client in db.MiniAppOAuthClients.AsNoTracking() on credential.OAuthClientId equals client.Id
            where credential.Id == prepared.SecretId && credential.RevokedAt == null && (credential.ExpiresAt == null || credential.ExpiresAt > now)
                && client.Id == prepared.ClientIdentity && client.ClientKey == prepared.ClientId && client.MiniAppId == app
                && client.Environment == MiniAppOAuthEnvironment.Production && client.RevokedAt == null select credential).SingleOrDefaultAsync(ct) ?? throw Conflict();
        return new(prepared.VersionId, prepared.Revision, prepared.ClientId, protector.Decrypt(secret.SecretEncrypted, secret.SecretKeyVersion, prepared.ClientIdentity));
    }
    private static DeveloperAuthException Conflict() => new(409, "CREDENTIAL_PREPARATION_CONFLICT", "Credential hoặc bản nháp đã thay đổi. Hãy tải lại.");
}
