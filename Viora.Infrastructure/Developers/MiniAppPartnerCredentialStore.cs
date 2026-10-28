using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppPartnerCredentialStore(AppDbContext db, MiniAppSecretProtector protector, TimeProvider clock)
{
    private const string Purpose = "mini-app-partner-credential";
    private static DeveloperAuthException InvalidClient() => new(401, "INVALID_CLIENT", "Thông tin backend Mini App không hợp lệ.");
    private static DeveloperAuthException Conflict() => new(409, "CREDENTIAL_CONFLICT", "Credential đã thay đổi. Vui lòng tải lại trước khi tiếp tục.");
    public static MiniAppOAuthEnvironment Environment(string? value) => value switch {
        "Sandbox" => MiniAppOAuthEnvironment.Sandbox,
        "Production" => MiniAppOAuthEnvironment.Production,
        _ => throw new DeveloperAuthException(400, "INVALID_ENVIRONMENT", "Chọn môi trường Sandbox hoặc Production.")
    };

    private void SetSecret(MiniAppPartnerCredential row, string secret)
    {
        var encrypted = protector.Encrypt(secret, row.Id, Purpose);
        row.SecretHash = MiniAppBridgeService.Hash(secret);
        row.SecretEncrypted = encrypted.Ciphertext; row.SecretKeyVersion = encrypted.Version;
        row.RevokedAt = null; row.Version = Guid.NewGuid();
    }

    public async Task<MiniAppPartnerCredential> GetOrCreateAsync(Guid appId, MiniAppOAuthEnvironment environment, CancellationToken ct)
    {
        var rows = db.Set<MiniAppPartnerCredential>();
        var row = await rows.SingleOrDefaultAsync(x => x.MiniAppId == appId && x.Environment == environment, ct);
        if (row is not null) return row;
        row = new() { MiniAppId = appId, Environment = environment };
        SetSecret(row, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)));
        rows.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) {
            // Concurrent first reveals share the unique app/environment row. Never
            // return a locally generated secret which failed to persist.
            db.Entry(row).State = EntityState.Detached;
            var winner = await rows.SingleOrDefaultAsync(x => x.MiniAppId == appId && x.Environment == environment, ct);
            if (winner is null) throw;
            return winner;
        }
        return row;
    }

    public MiniAppCredentialValue Value(string client, MiniAppPartnerCredential row)
    {
        var secret = row.RevokedAt is not null || row.SecretEncrypted.Length == 0 ? ""
            : protector.Decrypt(row.SecretEncrypted, row.SecretKeyVersion, row.Id, Purpose);
        return new(client, row.Environment.ToString(), secret, row.Version, row.RevokedAt is not null);
    }

    public async Task<MiniAppPartnerCredential> ChangeAsync(Guid appId, MiniAppOAuthEnvironment environment, Guid version, bool revoke, CancellationToken ct)
    {
        var row = await db.Set<MiniAppPartnerCredential>().SingleOrDefaultAsync(x => x.MiniAppId == appId && x.Environment == environment, ct);
        if (row is null || version == Guid.Empty || row.Version != version) throw Conflict();
        if (revoke) {
            row.RevokedAt = clock.GetUtcNow().UtcDateTime; row.Version = Guid.NewGuid();
            row.SecretHash = ""; row.SecretEncrypted = ""; row.SecretKeyVersion = "";
        } else SetSecret(row, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)));
        return row;
    }

    public async Task ValidateAsync(string client, string secret, bool sandbox, CancellationToken ct)
    {
        var environment = sandbox ? MiniAppOAuthEnvironment.Sandbox : MiniAppOAuthEnvironment.Production;
        var hash = await db.Set<MiniAppPartnerCredential>().AsNoTracking()
            .Where(x => x.MiniApp.AppId == client && x.MiniApp.DeletedAt == null && x.Environment == environment && x.RevokedAt == null)
            .Select(x => x.SecretHash).SingleOrDefaultAsync(ct);
        if (secret.Length is < 32 or > 256 || hash?.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(MiniAppBridgeService.Hash(secret)), Encoding.ASCII.GetBytes(hash)))
            throw InvalidClient();
    }

    public Task<bool> AvailableAsync(Guid appId, bool sandbox, CancellationToken ct)
    {
        var environment = sandbox ? MiniAppOAuthEnvironment.Sandbox : MiniAppOAuthEnvironment.Production;
        return db.Set<MiniAppPartnerCredential>().AnyAsync(x => x.MiniAppId == appId && x.Environment == environment && x.RevokedAt == null && x.SecretHash.Length == 64, ct);
    }

    // Explicit one-time operator import. Runtime auth never falls back to env.
    public async Task<int> ImportLegacyAsync(MiniAppBridgeOptions options, CancellationToken ct)
    {
        var plan = new List<MiniAppPartnerCredential>(); var clients = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in options.Partners) {
            var client = string.IsNullOrEmpty(pair.Value.ClientId) ? pair.Key : pair.Value.ClientId;
            if (!clients.Add(client)) throw new InvalidOperationException("Duplicate legacy Mini App Client ID.");
            var appId = await db.MiniApps.Where(x => x.AppId == client && x.DeletedAt == null).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Legacy credential references an unknown Mini App.");
            foreach (var environment in new[] { MiniAppOAuthEnvironment.Sandbox, MiniAppOAuthEnvironment.Production }) {
                var hash = environment == MiniAppOAuthEnvironment.Sandbox ? pair.Value.SandboxSecretHash : pair.Value.ProductionSecretHash;
                var secret = environment == MiniAppOAuthEnvironment.Sandbox ? pair.Value.SandboxSecret : pair.Value.ProductionSecret;
                if (string.IsNullOrEmpty(hash) && string.IsNullOrEmpty(secret)) continue;
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !string.IsNullOrEmpty(secret) &&
                    (secret.Length is < 32 or > 256 || secret.Any(char.IsControl) || MiniAppBridgeService.Hash(secret) != hash.ToUpperInvariant()))
                    throw new InvalidOperationException("Legacy credential hash/secret mismatch; nothing imported.");
                if (await db.Set<MiniAppPartnerCredential>().AnyAsync(x => x.MiniAppId == appId && x.Environment == environment, ct)) continue;
                var row = new MiniAppPartnerCredential { MiniAppId = appId, Environment = environment, SecretHash = hash.ToUpperInvariant() };
                if (!string.IsNullOrEmpty(secret)) SetSecret(row, secret);
                plan.Add(row);
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.AddRange(plan); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return plan.Count;
    }

    public static DeveloperAuthException ConcurrencyError() => Conflict();
}
