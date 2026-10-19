using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxAccess(AppDbContext db, TimeProvider clock, MiniAppSandboxOptions options)
{
    public async Task<MiniApp> RequireAsync(SandboxActor actor, string appId, CancellationToken ct, bool allowUnavailableApp=false)
    {
        if (!options.Enabled) throw new DeveloperAuthException(503,"SANDBOX_DISABLED","Sandbox chưa được bật.");
        var app = await db.MiniApps.AsNoTracking().Include(x=>x.Developer).SingleOrDefaultAsync(x=>x.AppId==appId && x.DeletedAt==null,ct);
        if (app==null || (actor.Kind==SandboxActorKind.Developer && app.DeveloperAccountId!=actor.Id)) throw NotFound();
        var now=clock.GetUtcNow().UtcDateTime;
        var active=actor.Kind switch
        {
            SandboxActorKind.Developer => await db.DeveloperSessions.AnyAsync(x=>x.Id==actor.SourceSessionId && x.DeveloperAccountId==actor.Id &&
                x.RevokedAt==null && x.ExpiresAt>now && x.AbsoluteExpiresAt>now && x.Account.Status==DeveloperStatus.Active,ct),
            SandboxActorKind.Admin => options.AdminTesterIds.Contains(actor.Id) &&
                await db.Accounts.AnyAsync(x=>x.Id==actor.Id && x.Role==AccountRole.Admin && x.Status==AccountStatus.Active,ct) &&
                await db.RefreshTokens.AnyAsync(x=>x.AccountId==actor.Id && x.SessionId==actor.SourceSessionId && x.RevokedAt==null && x.ExpiresAt>now,ct),
            _ => false
        };
        if (!active) throw new DeveloperAuthException(403,"SANDBOX_ACTOR_UNAVAILABLE","Phiên kiểm thử không còn quyền truy cập.");
        if (!allowUnavailableApp && (app.Developer.Status!=DeveloperStatus.Active || app.Status is MiniAppStatus.Suspended or MiniAppStatus.Rejected))
            throw new DeveloperAuthException(403,"SANDBOX_APP_UNAVAILABLE","Mini App chưa được phép chạy Sandbox.");
        return app;
    }
    public static DeveloperAuthException NotFound() => new(404,"SANDBOX_NOT_FOUND","Không tìm thấy tài nguyên Sandbox.");
}
