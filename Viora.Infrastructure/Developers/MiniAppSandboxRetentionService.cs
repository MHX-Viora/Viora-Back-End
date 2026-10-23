using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed record SandboxRetentionSummary(int RevokedSessions,int EndedRuns,int PurgedDiagnostics,int PurgedRuns,int PurgedGrants,int PurgedSessions);

public sealed class MiniAppSandboxRetentionService(AppDbContext db,TimeProvider clock)
{
    public async Task<SandboxRetentionSummary> SweepAsync(int batchSize,CancellationToken ct)
    {
        if(batchSize is <1 or >100)throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now=clock.GetUtcNow().UtcDateTime;var diagnosticsBefore=now.AddDays(-7);var summariesBefore=now.AddDays(-30);var version=Guid.NewGuid();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var expired=await db.MiniAppSandboxSessions.Where(x=>x.RevokedAt==null&&x.ExpiresAt<=now).OrderBy(x=>x.ExpiresAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var revoked=await db.MiniAppSandboxSessions.Where(x=>expired.Contains(x.Id)&&x.RevokedAt==null).ExecuteUpdateAsync(s=>s
            .SetProperty(x=>x.RevokedAt,now).SetProperty(x=>x.RuntimeCredentialHash,(string?)null).SetProperty(x=>x.Version,version),ct);
        var endedSessions=db.MiniAppSandboxSessions.Where(x=>x.RevokedAt!=null||x.ExpiresAt<=now);
        var expiredGrants=await db.MiniAppSandboxLaunchGrants.Where(x=>x.ConsumedAt==null&&(x.ExpiresAt<=now||endedSessions.Any(session=>session.Id==x.SandboxSessionId)))
            .OrderBy(x=>x.ExpiresAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        await db.MiniAppSandboxLaunchGrants.Where(x=>expiredGrants.Contains(x.Id)).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now).SetProperty(x=>x.Version,version),ct);
        var active=db.MiniAppSandboxTestRuns.Where(x=>x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running);
        var runIds=await active.Where(x=>x.DeadlineAt<=now||endedSessions.Any(session=>session.Id==x.SandboxSessionId))
            .OrderBy(x=>x.DeadlineAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var endedRuns=await active.Where(x=>runIds.Contains(x.Id)).ExecuteUpdateAsync(s=>s
            .SetProperty(x=>x.Status,x=>x.DeadlineAt<=now?SandboxTestRunStatus.Failed:SandboxTestRunStatus.Cancelled)
            .SetProperty(x=>x.SummaryCode,x=>x.DeadlineAt<=now?SandboxMessageCode.RunFailed:SandboxMessageCode.RunCancelled)
            .SetProperty(x=>x.CompletedAt,x=>x.StartedAt!=null&&x.StartedAt>now?x.StartedAt:x.CreatedAt>now?x.CreatedAt:now).SetProperty(x=>x.Version,version),ct);
        var pendingResults=await db.MiniAppSandboxTestResults.Where(x=>x.MessageCode==SandboxMessageCode.CheckPending&&
            db.MiniAppSandboxTestRuns.Any(r=>r.Id==x.TestRunId&&r.Status!=SandboxTestRunStatus.Queued&&r.Status!=SandboxTestRunStatus.Running))
            .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        await db.MiniAppSandboxTestResults.Where(x=>pendingResults.Contains(x.Id)).ExecuteUpdateAsync(s=>s
            .SetProperty(x=>x.Status,SandboxCheckStatus.Skipped).SetProperty(x=>x.MessageCode,SandboxMessageCode.MissingEvidence).SetProperty(x=>x.Version,version),ct);
        var diagnosticIds=await db.MiniAppSandboxDiagnostics.Where(x=>x.CreatedAt<=diagnosticsBefore&&endedSessions.Any(session=>session.Id==x.SandboxSessionId))
            .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var purgedDiagnostics=await db.MiniAppSandboxDiagnostics.Where(x=>diagnosticIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var oldRuns=db.MiniAppSandboxTestRuns.Where(x=>x.CompletedAt!=null&&x.CompletedAt<=summariesBefore&&endedSessions.Any(session=>session.Id==x.SandboxSessionId));
        var oldRunIds=await oldRuns.OrderBy(x=>x.CompletedAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var evidenceIds=await db.MiniAppSandboxProtocolEvidence.Where(x=>oldRunIds.Contains(x.TestRunId)).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        await db.MiniAppSandboxProtocolEvidence.Where(x=>evidenceIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var resultIds=await db.MiniAppSandboxTestResults.Where(x=>oldRunIds.Contains(x.TestRunId)&&!db.MiniAppSandboxProtocolEvidence.Any(e=>e.TestRunId==x.TestRunId&&e.TestKey==x.TestKey))
            .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        await db.MiniAppSandboxTestResults.Where(x=>resultIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var executorGrantIds=await db.MiniAppSandboxLaunchGrants.Where(x=>x.ExecutorRunId!=null&&oldRunIds.Contains(x.ExecutorRunId.Value)&&x.ExpiresAt<=summariesBefore)
            .OrderBy(x=>x.ExpiresAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var purgedExecutorGrants=await db.MiniAppSandboxLaunchGrants.Where(x=>executorGrantIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var purgedRuns=await oldRuns.Where(x=>oldRunIds.Contains(x.Id)&&!db.MiniAppSandboxTestResults.Any(r=>r.TestRunId==x.Id)&&!db.MiniAppSandboxDiagnostics.Any(d=>d.TestRunId==x.Id)&&
                !db.MiniAppSandboxLaunchGrants.Any(g=>g.ExecutorRunId==x.Id))
            .ExecuteDeleteAsync(ct);
        var grantIds=await db.MiniAppSandboxLaunchGrants.Where(x=>x.ExpiresAt<=summariesBefore&&endedSessions.Any(session=>session.Id==x.SandboxSessionId))
            .OrderBy(x=>x.ExpiresAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var purgedGrants=await db.MiniAppSandboxLaunchGrants.Where(x=>grantIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var sessionIds=await db.MiniAppSandboxSessions.Where(x=>(x.ExpiresAt<=summariesBefore||x.RevokedAt!=null&&x.RevokedAt<=summariesBefore)&&
            !db.MiniAppSandboxTestRuns.Any(r=>r.SandboxSessionId==x.Id)&&!db.MiniAppSandboxDiagnostics.Any(d=>d.SandboxSessionId==x.Id)&&!db.MiniAppSandboxLaunchGrants.Any(g=>g.SandboxSessionId==x.Id))
            .OrderBy(x=>x.ExpiresAt).ThenBy(x=>x.Id).Take(batchSize).Select(x=>x.Id).ToArrayAsync(ct);
        var purgedSessions=await db.MiniAppSandboxSessions.Where(x=>sessionIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);return new(revoked,endedRuns,purgedDiagnostics,purgedRuns,purgedGrants+purgedExecutorGrants,purgedSessions);
    }
}
