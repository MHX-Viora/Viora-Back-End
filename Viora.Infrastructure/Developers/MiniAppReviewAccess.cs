using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppReviewAccess(AppDbContext db, TimeProvider clock)
{
    public static DeveloperAuthException NotFound() => new(404, "MINI_APP_REVIEW_NOT_FOUND", "Không tìm thấy tài nguyên Mini App.");
    public static DeveloperAuthException Forbidden() => new(403, "MINI_APP_REVIEW_FORBIDDEN", "Phiên hiện tại không có quyền thực hiện thao tác này.");

    public async Task<MiniApp> RequireOwnerAsync(Guid owner, Guid sourceSession, string appId, CancellationToken ct)
    {
        var app = await db.MiniApps.AsNoTracking().SingleOrDefaultAsync(
            x => x.AppId == appId && x.DeveloperAccountId == owner && x.DeletedAt == null, ct) ?? throw NotFound();
        await RequireDeveloperAsync(owner, sourceSession, ct);
        return app;
    }
    public async Task RequireDeveloperAsync(Guid owner, Guid sourceSession, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (owner == Guid.Empty || sourceSession == Guid.Empty || !await db.DeveloperSessions.AnyAsync(x =>
            x.Id == sourceSession && x.DeveloperAccountId == owner && x.RevokedAt == null &&
            x.ExpiresAt > now && x.AbsoluteExpiresAt > now && x.Account.Status == DeveloperStatus.Active &&
            (x.Account.LockedUntil == null || x.Account.LockedUntil <= now), ct)) throw Forbidden();
    }

    public async Task<bool> IsLiveAdminAsync(Guid account, Guid session, CancellationToken ct)
    {
        if (account == Guid.Empty || session == Guid.Empty) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.RefreshTokens.AnyAsync(x => x.AccountId == account && x.SessionId == session &&
            x.RevokedAt == null && x.ExpiresAt > now && x.Account.Role == AccountRole.Admin &&
            x.Account.Status == AccountStatus.Active && x.Account.DeletedAt == null, ct);
    }

    public async Task RequireAdminAsync(Guid account, Guid session, CancellationToken ct)
    {
        if (!await IsLiveAdminAsync(account, session, ct)) throw Forbidden();
    }
}
