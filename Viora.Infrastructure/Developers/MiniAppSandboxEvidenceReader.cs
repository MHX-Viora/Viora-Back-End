using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxEvidenceReader(AppDbContext db,MiniAppSandboxSessionService sessions,MiniAppSandboxOptions options,TimeProvider clock)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    public static bool Handles(SandboxCheckKey key)=>key is SandboxCheckKey.Runtime or SandboxCheckKey.Bridge;
    private bool Browser(SandboxCheckKey key)=>key is SandboxCheckKey.Runtime or SandboxCheckKey.Bridge;
    private SandboxCheckExecution Missing(SandboxCheckKey key)=>Browser(key)&&options.ExecutorKey.Length==0?
        new(SandboxCheckStatus.NotImplemented,SandboxMessageCode.ExecutorUnavailable,SandboxEvidenceSource.Server):
        new(SandboxCheckStatus.Skipped,SandboxMessageCode.MissingEvidence,SandboxEvidenceSource.Server);
    public async Task<SandboxCheckExecution?> ReadAsync(Guid runId,SandboxCheckKey key,CancellationToken ct)
    {
        if(!Handles(key)||Browser(key)&&options.ExecutorKey.Length==0)return null;
        var now=Now;
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId&&
            (x.Status==SandboxTestRunStatus.Running&&x.DeadlineAt>now||x.Status==SandboxTestRunStatus.Completed&&x.CompletedAt>=now.AddMinutes(-5)),ct);
        if(run==null)return null;
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleAsync(x=>x.Id==run.SandboxSessionId,ct);
        if(run.ConfigurationFingerprint!=session.ConfigurationFingerprint)return null;
        var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
        try{await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);}catch(DeveloperAuthException){return null;}
        var cutoff=new[]{run.CreatedAt.AddSeconds(-1),now.AddMinutes(-5)}.Max();var ceiling=new[]{now.AddSeconds(1),run.DeadlineAt}.Min();
        if(Browser(key)){
            var category=key==SandboxCheckKey.Runtime?SandboxDiagnosticCategory.Runtime:SandboxDiagnosticCategory.Bridge;
            var passed=key==SandboxCheckKey.Runtime?SandboxMessageCode.RuntimeReady:SandboxMessageCode.BridgeReady;
            var failed=key==SandboxCheckKey.Runtime?SandboxMessageCode.RuntimeError:SandboxMessageCode.BridgeError;
            var row=await db.MiniAppSandboxDiagnostics.AsNoTracking().Where(x=>x.TestRunId==runId&&x.SandboxSessionId==session.Id&&x.EvidenceSource==SandboxEvidenceSource.TrustedBrowser&&
                x.Category==category&&(x.Code==passed||x.Code==failed)&&x.CreatedAt>=cutoff&&x.CreatedAt<=ceiling).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync(ct);
            return row==null?null:new(row.Code==passed?SandboxCheckStatus.Passed:SandboxCheckStatus.Failed,
                row.Code==passed?SandboxMessageCode.CheckPassed:SandboxMessageCode.CheckFailed,SandboxEvidenceSource.TrustedBrowser,row.CreatedAt);
        }
        return null;
    }
    public Task<Guid?> LatestRunIdAsync(Guid appId,string fingerprint,CancellationToken ct)=>db.MiniAppSandboxTestRuns.AsNoTracking()
        .Where(x=>x.ConfigurationFingerprint==fingerprint&&db.MiniAppSandboxSessions.Any(s=>s.Id==x.SandboxSessionId&&s.MiniAppId==appId))
        .OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
    public async Task<bool> IsFreshAsync(Guid runId,string fingerprint,CancellationToken ct)
    {
        var now=Now;
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId&&x.Status==SandboxTestRunStatus.Completed&&x.ConfigurationFingerprint==fingerprint&&x.CompletedAt>=now.AddMinutes(-5),ct);
        if(run==null)return false;
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleAsync(x=>x.Id==run.SandboxSessionId,ct);
        if(session.ConfigurationFingerprint!=fingerprint)return false;
        var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
        try{await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);return true;}catch(DeveloperAuthException){return false;}
    }
    public async Task<SandboxCheckExecution> LatestAsync(Guid appId,string fingerprint,SandboxCheckKey key,CancellationToken ct,Guid? pinnedRun=null)
    {
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().Where(x=>(pinnedRun==null||x.Id==pinnedRun)&&x.ConfigurationFingerprint==fingerprint&&db.MiniAppSandboxSessions.Any(s=>s.Id==x.SandboxSessionId&&s.MiniAppId==appId))
            .OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        if(run?.Status!=SandboxTestRunStatus.Completed)return Missing(key);
        var result=await db.MiniAppSandboxTestResults.AsNoTracking().SingleOrDefaultAsync(x=>x.TestRunId==run.Id&&x.TestKey==key,ct);
        var proof=await ReadAsync(run.Id,key,ct);
        if(proof==null||result==null||result.Status!=proof.Status||result.EvidenceSource!=proof.EvidenceSource||result.EvidenceAt!=proof.EvidenceAt)return Missing(key);
        return proof;
    }
    public async Task<SandboxCheckExecution> WaitAsync(Guid sessionId,SandboxCheckKey key,CancellationToken ct)
    {
        if(Browser(key)&&options.ExecutorKey.Length==0)return Missing(key);
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.SandboxSessionId==sessionId&&x.Status==SandboxTestRunStatus.Running,ct);
        if(run==null)return Missing(key);
        var stop=new[]{Now.AddSeconds(40),run.DeadlineAt.AddSeconds(-1)}.Min();
        do{
            var proof=await ReadAsync(run.Id,key,ct);if(proof!=null)return proof;
            if(!await db.MiniAppSandboxTestRuns.AnyAsync(x=>x.Id==run.Id&&x.Version==run.Version&&x.Status==SandboxTestRunStatus.Running,ct))return Missing(key);
            await Task.Delay(TimeSpan.FromMilliseconds(250),clock,ct);
        }while(Now<stop);
        return Missing(key);
    }
}
