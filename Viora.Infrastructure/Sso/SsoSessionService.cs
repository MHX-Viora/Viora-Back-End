using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed record IssuedSsoSession(string Credential, DateTime ExpiresAt);

public sealed class SsoSessionService(AppDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public async Task<IssuedSsoSession> CreateAsync(Guid accountId, Guid socialSessionId, CancellationToken ct)
    {
        var now = Now;
        var expiresAt = await ValidateSocialSessionAsync(accountId, socialSessionId, ct);
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        db.AnktSsoSessions.Add(new AnktSsoSession { AccountId = accountId, SocialSessionId = socialSessionId, TokenHash = Hash(raw), ExpiresAt = expiresAt });
        await db.SaveChangesAsync(ct);
        return new(raw, expiresAt);
    }

    public async Task<DateTime> ValidateSocialSessionAsync(Guid accountId, Guid socialSessionId, CancellationToken ct)
    {
        var now = Now;
        var expiry = await db.RefreshTokens.Where(x => x.AccountId == accountId && x.SessionId == socialSessionId && x.RevokedAt == null && x.ExpiresAt > now)
            .Select(x => (DateTime?)x.ExpiresAt).MaxAsync(ct);
        if (expiry == null || !await ActiveAccount(accountId, ct))
            throw new DeveloperAuthException(401, "SSO_SESSION_INVALID", "Phiên ANKT đã hết hạn. Vui lòng đăng nhập lại.");
        // Stored session timestamps are UTC; some providers omit DateTime.Kind.
        // Preserve that contract before callers construct cookie DateTimeOffsets.
        return DateTime.SpecifyKind(expiry.Value < now.AddHours(8) ? expiry.Value : now.AddHours(8), DateTimeKind.Utc);
    }

    public async Task<AnktSsoSession?> AuthenticateAsync(string? credential, CancellationToken ct)
    {
        if (credential is not { Length: 86 } || credential.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_'))) return null;
        var now = Now; var hash = Hash(credential);
        var session = await db.AnktSsoSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > now, ct);
        if (session == null || !await ActiveAccount(session.AccountId, ct)) return null;
        return await db.RefreshTokens.AnyAsync(x => x.AccountId == session.AccountId && x.SessionId == session.SocialSessionId && x.RevokedAt == null && x.ExpiresAt > now, ct) ? session : null;
    }

    public async Task RevokeAsync(string credential, CancellationToken ct)
    {
        var hash = Hash(credential);
        await db.AnktSsoSessions.Where(x => x.TokenHash == hash && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, Now), ct);
    }

    private Task<bool> ActiveAccount(Guid id, CancellationToken ct) => db.Accounts.AsNoTracking().AnyAsync(x => x.Id == id && x.Status == AccountStatus.Active && x.DeletedAt == null, ct);
}
