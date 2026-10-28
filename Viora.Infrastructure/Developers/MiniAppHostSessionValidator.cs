using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// Revalidate the existing ANKT session without creating another login credential.
public static class MiniAppHostSessionValidator
{
    public static async Task<DateTime> ValidateAsync(AppDbContext db, TimeProvider clock,
        Guid accountId, Guid socialSessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expiry = await db.RefreshTokens
            .Where(x => x.AccountId == accountId && x.SessionId == socialSessionId && x.RevokedAt == null && x.ExpiresAt > now)
            .Select(x => (DateTime?)x.ExpiresAt).MaxAsync(ct);
        if (expiry == null || !await db.Accounts.AsNoTracking().AnyAsync(x =>
                x.Id == accountId && !x.IsSandbox && x.Status == AccountStatus.Active && x.DeletedAt == null, ct))
            throw new DeveloperAuthException(401, "SESSION_EXPIRED", "Phiên ANKT đã hết hạn. Vui lòng đăng nhập lại.");
        return DateTime.SpecifyKind(expiry.Value < now.AddHours(8) ? expiry.Value : now.AddHours(8), DateTimeKind.Utc);
    }
}
