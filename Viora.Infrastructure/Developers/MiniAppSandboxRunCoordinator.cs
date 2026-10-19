using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxRunCoordinator(AppDbContext db,MiniAppSandboxSessionService sessions,TimeProvider clock,
    IMiniAppSandboxCheckExecutor checks,MiniAppSandboxRunCancellation cancellation)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private static string Key(SandboxCheckKey key){var value=key.ToString();return char.ToLowerInvariant(value[0])+value[1..];}
    private static SandboxCheckKey[] Keys(string[]? input)
    {
        var catalog=Enum.GetValues<SandboxCheckKey>();if(input==null)return catalog;
        if(input.Length<1||input.Length>catalog.Length||input.Distinct(StringComparer.Ordinal).Count()!=input.Length||input.Any(value=>!catalog.Any(key=>Key(key)==value)))
            throw MiniAppInput.Invalid("Chọn các check Sandbox hợp lệ, không trùng lặp.");
        return input.Select(value=>catalog.Single(key=>Key(key)==value)).ToArray();
    }
    private static SandboxRunSummary Dto(MiniAppSandboxTestRun run)=>new(run.Id,run.SandboxSessionId,"Sandbox",run.ConfigurationFingerprint,run.Status.ToString(),
        MiniAppSandboxDiagnosticCatalog.Code(run.SummaryCode),DateTime.SpecifyKind(run.CreatedAt,DateTimeKind.Utc),DateTime.SpecifyKind(run.DeadlineAt,DateTimeKind.Utc),
        run.StartedAt is {} started?DateTime.SpecifyKind(started,DateTimeKind.Utc):null,run.CompletedAt is {} ended?DateTime.SpecifyKind(ended,DateTimeKind.Utc):null);

    public async Task<SandboxRunSummary> CreateAsync(SandboxActor actor,string appId,SandboxRunRequest request,CancellationToken ct)
    {
        var selected=Keys(request.Checks);
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var session=await sessions.RequireSessionAsync(actor,appId,request.SessionId,ct);var now=Now;
            await ReconcileAsync(ct);
            if(await db.MiniAppSandboxTestRuns.CountAsync(x=>x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running,ct)>=MiniAppSandboxRunCancellation.Capacity)
                throw new DeveloperAuthException(429,"SANDBOX_QUEUE_FULL","Hàng đợi Sandbox đã đầy. Vui lòng thử lại sau.");
            if(await db.MiniAppSandboxTestRuns.AnyAsync(x=>x.SandboxSessionId==session.Id&&(x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running),ct))
                throw new DeveloperAuthException(409,"SANDBOX_RUN_ACTIVE","Phiên đã có một lượt kiểm tra đang chờ hoặc chạy.");
            var deadline=session.ExpiresAt<now.AddSeconds(120)?session.ExpiresAt:now.AddSeconds(120);
            if(deadline<=now.AddSeconds(3))throw new DeveloperAuthException(410,"SANDBOX_SESSION_ENDED","Phiên sắp hết hạn. Vui lòng mở phiên mới.");
            var run=new MiniAppSandboxTestRun {SandboxSessionId=session.Id,ConfigurationFingerprint=session.ConfigurationFingerprint,DeadlineAt=deadline};
            db.MiniAppSandboxTestRuns.Add(run);
            db.MiniAppSandboxTestResults.AddRange(selected.Select(key=>new MiniAppSandboxTestResult {TestRunId=run.Id,TestKey=key}));
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Dto(run);
        }
        catch(Exception error) when(Conflict(error)){throw new DeveloperAuthException(409,"SANDBOX_CONFLICT","Lượt kiểm tra vừa thay đổi. Vui lòng tải lại.");}
    }
    private async Task<MiniAppSandboxTestRun> Owned(SandboxActor actor,string appId,Guid id,CancellationToken ct)
    {
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw MiniAppSandboxAccess.NotFound();
        await sessions.GetAsync(actor,appId,run.SandboxSessionId,ct);return run;
    }
    public async Task<SandboxRunDetail> GetAsync(SandboxActor actor,string appId,Guid id,CancellationToken ct)
    {
        var run=await Owned(actor,appId,id,ct);var results=await db.MiniAppSandboxTestResults.AsNoTracking().Where(x=>x.TestRunId==id).OrderBy(x=>x.TestKey).ToArrayAsync(ct);
        return new(Dto(run),results.Select(x=>new SandboxCheckResultDto(Key(x.TestKey),x.Status.ToString(),MiniAppSandboxDiagnosticCatalog.Code(x.MessageCode),MiniAppSandboxDiagnosticCatalog.Source(x.EvidenceSource),
            x.EvidenceAt is {} time?DateTime.SpecifyKind(time,DateTimeKind.Utc):null,x.DurationMs)).ToArray());
    }
    public async Task<SandboxRunPage> ListAsync(SandboxActor actor,string appId,Guid sessionId,string? cursor,int limit,CancellationToken ct)
    {
        await sessions.GetAsync(actor,appId,sessionId,ct);if(limit is <1 or >100)throw MiniAppInput.Invalid("Giới hạn trang từ 1 đến 100.");
        var query=db.MiniAppSandboxTestRuns.AsNoTracking().Where(x=>x.SandboxSessionId==sessionId);
        if(cursor!=null)
        {
            if(!Guid.TryParseExact(cursor,"N",out var id))throw MiniAppInput.Invalid("Cursor lượt kiểm tra không hợp lệ.");
            var anchor=await query.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw MiniAppInput.Invalid("Cursor lượt kiểm tra không còn hợp lệ.");var anchorId=anchor.Id.ToString();
            query=query.Where(x=>x.CreatedAt>anchor.CreatedAt||x.CreatedAt==anchor.CreatedAt&&string.Compare(x.Id.ToString().ToLower(),anchorId)>0);
        }
        var rows=await query.OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id.ToString().ToLower()).Take(limit+1).ToArrayAsync(ct);
        return new(rows.Take(limit).Select(Dto).ToArray(),rows.Length>limit?rows[limit-1].Id.ToString("N"):null);
    }
    public async Task CancelAsync(SandboxActor actor,string appId,Guid id,CancellationToken ct)
    {
        await Owned(actor,appId,id,ct);
        await EndAsync(id,null,SandboxTestRunStatus.Cancelled,SandboxMessageCode.RunCancelled,ct);
        cancellation.Cancel(id);
    }
    private async Task EndAsync(Guid id,Guid? version,SandboxTestRunStatus status,SandboxMessageCode code,CancellationToken ct)
    {
        var now=Now;var next=Guid.NewGuid();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var changed=await db.MiniAppSandboxTestRuns.Where(x=>x.Id==id&&(version==null||x.Version==version)&&(x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running)&&
                (status!=SandboxTestRunStatus.Completed||x.DeadlineAt>now))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,status).SetProperty(x=>x.SummaryCode,code)
                .SetProperty(x=>x.CompletedAt,x=>x.StartedAt!=null&&x.StartedAt>now?x.StartedAt:x.CreatedAt>now?x.CreatedAt:now).SetProperty(x=>x.Version,next),ct);
        if(changed!=0)await db.MiniAppSandboxTestResults.Where(x=>x.TestRunId==id&&x.MessageCode==SandboxMessageCode.CheckPending)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxCheckStatus.Skipped).SetProperty(x=>x.MessageCode,SandboxMessageCode.MissingEvidence).SetProperty(x=>x.Version,next),ct);
        await tx.CommitAsync(ct);
    }
    private async Task ReconcileAsync(CancellationToken ct)
    {
        var now=Now;var version=Guid.NewGuid();
        var ids=await db.MiniAppSandboxTestRuns.Where(x=>(x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running)&&x.DeadlineAt<=now)
            .OrderBy(x=>x.DeadlineAt).Take(100).Select(x=>x.Id).ToArrayAsync(ct);
        await db.MiniAppSandboxTestRuns.Where(x=>ids.Contains(x.Id)).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxTestRunStatus.Failed)
            .SetProperty(x=>x.SummaryCode,SandboxMessageCode.RunFailed).SetProperty(x=>x.CompletedAt,x=>x.StartedAt!=null&&x.StartedAt>now?x.StartedAt:x.CreatedAt>now?x.CreatedAt:now).SetProperty(x=>x.Version,version),ct);
        await db.MiniAppSandboxTestResults.Where(x=>ids.Contains(x.TestRunId)&&x.MessageCode==SandboxMessageCode.CheckPending)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxCheckStatus.Skipped).SetProperty(x=>x.MessageCode,SandboxMessageCode.MissingEvidence).SetProperty(x=>x.Version,version),ct);
    }
    private async Task<MiniAppSandboxTestRun?> ClaimAsync(CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);await ReconcileAsync(ct);
        var now=Now;var run=await db.MiniAppSandboxTestRuns.AsNoTracking().Where(x=>x.Status==SandboxTestRunStatus.Queued&&x.DeadlineAt>now).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        if(run==null){await tx.CommitAsync(ct);return null;}
        var version=Guid.NewGuid();var started=run.CreatedAt>now?run.CreatedAt:now;
        var changed=await db.MiniAppSandboxTestRuns.Where(x=>x.Id==run.Id&&x.Status==SandboxTestRunStatus.Queued&&x.Version==run.Version)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxTestRunStatus.Running).SetProperty(x=>x.SummaryCode,SandboxMessageCode.RunStarted).SetProperty(x=>x.StartedAt,started).SetProperty(x=>x.Version,version),ct);
        await tx.CommitAsync(ct);if(changed==0)return null;run.Version=version;return run;
    }
    public async Task<bool> ExecuteNextAsync(CancellationToken ct)
    {
        MiniAppSandboxTestRun? run;
        try{run=await ClaimAsync(ct);}catch(Exception error) when(Conflict(error)){return false;}
        if(run==null)return false;
        var remaining=run.DeadlineAt-Now;
        using var timeout=new CancellationTokenSource(remaining>TimeSpan.Zero?remaining:TimeSpan.Zero,clock);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,timeout.Token);
        var local=cancellation.Register(run.Id,run.SandboxSessionId,linked.Token);
        try
        {
            var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleAsync(x=>x.Id==run.SandboxSessionId,local.Token);
            var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(local.Token);var actor=MiniAppSandboxSessionService.Actor(session);
            var selected=await db.MiniAppSandboxTestResults.Where(x=>x.TestRunId==run.Id).OrderBy(x=>x.TestKey).Select(x=>x.TestKey).ToArrayAsync(local.Token);
            foreach(var key in selected)
            {
                local.Token.ThrowIfCancellationRequested();await sessions.RequireSessionAsync(actor,appId,session.Id,local.Token);
                if(!await CurrentAsync(run,local.Token)){await EndAsync(run.Id,run.Version,SandboxTestRunStatus.Failed,SandboxMessageCode.RunFailed,local.Token);return true;}
                var start=Stopwatch.GetTimestamp();var result=await checks.ExecuteAsync(session.Id,key,local.Token).WaitAsync(local.Token);
                local.Token.ThrowIfCancellationRequested();await sessions.RequireSessionAsync(actor,appId,session.Id,local.Token);
                var now=Now;var duration=(int)Math.Min(1800000,Stopwatch.GetElapsedTime(start).TotalMilliseconds);var version=Guid.NewGuid();
                if(!Enum.IsDefined(result.Status)||!Enum.IsDefined(result.MessageCode)||!Enum.IsDefined(result.EvidenceSource)||result.EvidenceAt>now)throw new InvalidOperationException("Invalid server check result.");
                var updated=await db.MiniAppSandboxTestResults.Where(x=>x.TestRunId==run.Id&&x.TestKey==key&&x.MessageCode==SandboxMessageCode.CheckPending&&
                    db.MiniAppSandboxTestRuns.Any(r=>r.Id==run.Id&&r.Version==run.Version&&r.Status==SandboxTestRunStatus.Running&&r.DeadlineAt>now))
                    .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,result.Status).SetProperty(x=>x.MessageCode,result.MessageCode).SetProperty(x=>x.EvidenceSource,result.EvidenceSource)
                        .SetProperty(x=>x.EvidenceAt,result.EvidenceAt).SetProperty(x=>x.DurationMs,duration).SetProperty(x=>x.Version,version),local.Token);
                if(updated==0){await EndAsync(run.Id,run.Version,SandboxTestRunStatus.Failed,SandboxMessageCode.RunFailed,local.Token);return true;}
            }
            await sessions.RequireSessionAsync(actor,appId,session.Id,local.Token);
            if(await CurrentAsync(run,local.Token))await EndAsync(run.Id,run.Version,SandboxTestRunStatus.Completed,SandboxMessageCode.RunCompleted,local.Token);
            else await EndAsync(run.Id,run.Version,SandboxTestRunStatus.Failed,SandboxMessageCode.RunFailed,local.Token);
        }
        catch(Exception)
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await EndAsync(run.Id,run.Version,SandboxTestRunStatus.Failed,SandboxMessageCode.RunFailed,cleanup.Token);
        }
        finally{cancellation.Release(run.Id,local);}
        return true;
    }
    private Task<bool> CurrentAsync(MiniAppSandboxTestRun run,CancellationToken ct)
    {var now=Now;return db.MiniAppSandboxTestRuns.AnyAsync(x=>x.Id==run.Id&&x.Status==SandboxTestRunStatus.Running&&x.Version==run.Version&&x.DeadlineAt>now,ct);}
    private static bool Conflict(Exception error)=>MiniAppSandboxSessionService.SerializationConflict(error)||error is DbUpdateException {InnerException:Npgsql.PostgresException {SqlState:"23505"}};
}
