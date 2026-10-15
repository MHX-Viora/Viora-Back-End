using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Developers;

public sealed partial class DeveloperIdentityService
{
    public async Task<DeveloperSessionIdentity?> AuthenticateAsync(string raw, CancellationToken ct)
    {
        if (raw.Length != 43) return null;
        var hash = Hash(raw); var now = Now;
        var session = await db.DeveloperSessions.AsNoTracking().Include(s => s.Account).SingleOrDefaultAsync(s =>
            s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now && s.AbsoluteExpiresAt > now, ct);
        return session is null || session.Account.Status != DeveloperStatus.Active || session.Account.EmailVerifiedAt is null
            ? null : new(session.Id, Map(session.Account));
    }

    public async Task<DeveloperLoginResult> RefreshAsync(Guid sessionId, CancellationToken ct)
    {
        var now = Now;
        var changed = await db.DeveloperSessions.Where(s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now && s.AbsoluteExpiresAt > now && s.Account.Status == DeveloperStatus.Active && s.Account.EmailVerifiedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.LastUsedAt, now)
                .SetProperty(s => s.ExpiresAt, s => s.AbsoluteExpiresAt < now.AddHours(8) ? s.AbsoluteExpiresAt : now.AddHours(8)), ct);
        if (changed != 1) throw InvalidCredentials();
        var session = await db.DeveloperSessions.AsNoTracking().Include(s => s.Account).SingleAsync(s => s.Id == sessionId, ct);
        return new(Map(session.Account), "", session.ExpiresAt, session.AbsoluteExpiresAt, session.AbsoluteExpiresAt > session.CreatedAt.AddHours(9));
    }

    public async Task LogoutAsync(Guid sessionId, bool all, DeveloperRequestContext context, CancellationToken ct)
    {
        var session = await db.DeveloperSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return;
        await db.DeveloperSessions.Where(s => s.DeveloperAccountId == session.DeveloperAccountId && (all || s.Id == sessionId) && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.RevokedAt, Now), ct);
        Audit(session.DeveloperAccountId, all ? "LoggedOutAll" : "LoggedOut", context); await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeveloperSessionDto>> SessionsAsync(Guid accountId, Guid sessionId, CancellationToken ct) =>
        await db.DeveloperSessions.AsNoTracking().Where(s => s.DeveloperAccountId == accountId && s.RevokedAt == null && s.ExpiresAt > Now)
            .OrderByDescending(s => s.CreatedAt).Select(s => new DeveloperSessionDto(s.Id, s.DeviceInfo, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.Id == sessionId)).ToListAsync(ct);

    public async Task RevokeSessionAsync(Guid accountId, Guid targetId, DeveloperRequestContext context, CancellationToken ct)
    {
        var changed = await db.DeveloperSessions.Where(s => s.Id == targetId && s.DeveloperAccountId == accountId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.RevokedAt, Now), ct);
        if (changed == 0) return;
        Audit(accountId, "SessionRevoked", context); await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeveloperIdentityDto>> LinkedAccountsAsync(Guid accountId, CancellationToken ct) =>
        await db.DeveloperExternalIdentities.AsNoTracking().Where(i => i.DeveloperAccountId == accountId)
            .Select(i => new DeveloperIdentityDto(i.Id, i.Provider, i.CreatedAt)).ToListAsync(ct);

    public async Task UnlinkAsync(Guid accountId, Guid identityId, string currentPassword, DeveloperRequestContext context, CancellationToken ct)
    {
        var account = await db.DeveloperAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct);
        if (account.PasswordHash is null)
            throw new DeveloperAuthException(409, "LAST_LOGIN_METHOD", "Cần thiết lập mật khẩu trước khi hủy liên kết phương thức đăng nhập.");
        await RequireCurrentPasswordAsync(account, currentPassword, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var version = Guid.NewGuid();
        var changed = await db.DeveloperAccounts.Where(a => a.Id == accountId && a.SecurityVersion == account.SecurityVersion && a.Status == DeveloperStatus.Active && (a.LockedUntil == null || a.LockedUntil <= Now))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SecurityVersion, version).SetProperty(a => a.UpdatedAt, Now), ct);
        if (changed != 1) throw InvalidCredentials();
        var deleted = await db.DeveloperExternalIdentities.Where(i => i.Id == identityId && i.DeveloperAccountId == accountId).ExecuteDeleteAsync(ct);
        if (deleted != 1) throw new DeveloperAuthException(404, "NOT_FOUND", "Không tìm thấy tài khoản liên kết.");
        await db.DeveloperSessions.Where(s => s.DeveloperAccountId == accountId && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(s => s.RevokedAt, Now), ct);
        Audit(accountId, "IdentityUnlinked", context); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
