using Viora.Application.Developers;
using Viora.Domain.Developers;
using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxCheckExecutor(AppDbContext db,MiniAppSandboxSessionService sessions,
    MiniAppSandboxConfigurationService configuration,MiniAppSandboxEvidenceReader? evidence=null) : IMiniAppSandboxCheckExecutor
{
    public async Task<SandboxCheckExecution> ExecuteAsync(Guid sessionId,SandboxCheckKey key,CancellationToken ct)
    {
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sessionId,ct)
            ?? throw MiniAppSandboxAccess.NotFound();
        var actor=MiniAppSandboxSessionService.Actor(session);
        var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
        await sessions.RequireSessionAsync(actor,appId,sessionId,ct);
        var sso=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.SsoEnabled).SingleAsync(ct);
        var result=evidence!=null&&MiniAppSandboxEvidenceReader.Handles(key)&&(sso||key<SandboxCheckKey.Consent)?
            await evidence.WaitAsync(sessionId,key,ct):await configuration.ExecuteAsync(session.MiniAppId,key,ct);
        await sessions.RequireSessionAsync(actor,appId,sessionId,ct);
        return result;
    }
}
