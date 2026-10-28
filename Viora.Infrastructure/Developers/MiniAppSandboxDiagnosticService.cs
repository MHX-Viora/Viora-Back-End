using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxDiagnosticService(AppDbContext db,MiniAppSandboxSessionService sessions,TimeProvider clock)
{
    public async Task<SandboxDiagnosticIngestion> IngestAsync(Guid sessionId,SandboxEvidenceSource source,SandboxDiagnosticBatch request,CancellationToken ct)
    {
        if(source is not (SandboxEvidenceSource.Host or SandboxEvidenceSource.Sdk) || request?.Events is not {Length:>=1 and <=20})
            throw MiniAppInput.Invalid("Cần từ 1 đến 20 sự kiện quan sát Sandbox.");
        var rows=request.Events.Select(MiniAppSandboxDiagnosticCatalog.Observation).ToArray();
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sessionId,ct)??throw MiniAppSandboxAccess.NotFound();
            var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
            await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,sessionId,ct);
            var now=clock.GetUtcNow().UtcDateTime;
            if(request.TestRunId is {} runId && !await db.MiniAppSandboxTestRuns.AnyAsync(x=>x.Id==runId && x.SandboxSessionId==sessionId && x.ConfigurationFingerprint==session.ConfigurationFingerprint &&
                (x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running) && x.DeadlineAt>now,ct))throw MiniAppSandboxAccess.NotFound();
            var query=db.MiniAppSandboxDiagnostics.Where(x=>x.SandboxSessionId==sessionId);
            var total=await query.CountAsync(ct);var cutoff=now.AddMinutes(-1);var recent=await query.CountAsync(x=>x.CreatedAt>=cutoff,ct);
            if(total+rows.Length>500 || recent+rows.Length>64)
            {
                var count=rows.Length;var version=Guid.NewGuid();
                await db.MiniAppSandboxSessions.Where(x=>x.Id==sessionId).ExecuteUpdateAsync(s=>s
                    .SetProperty(x=>x.DroppedDiagnosticCount,x=>x.DroppedDiagnosticCount>1000000-count?1000000:x.DroppedDiagnosticCount+count).SetProperty(x=>x.Version,version),ct);
                await tx.CommitAsync(ct);
                throw new DeveloperAuthException(429,"SANDBOX_DIAGNOSTIC_LIMIT","Diagnostic vượt giới hạn. Vui lòng chờ trước khi gửi lại.");
            }
            foreach(var row in rows){row.SandboxSessionId=sessionId;row.TestRunId=request.TestRunId;row.EvidenceSource=source;}
            db.MiniAppSandboxDiagnostics.AddRange(rows);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return new(rows.Length);
        }
        catch(Exception error) when(MiniAppSandboxSessionService.SerializationConflict(error))
        {throw new DeveloperAuthException(409,"SANDBOX_CONFLICT","Phiên vừa thay đổi. Vui lòng thử lại.");}
    }

    public async Task<SandboxDiagnosticsPage> ListAsync(SandboxActor actor,string appId,Guid sessionId,string? category,string? cursor,int limit,CancellationToken ct)
    {
        await sessions.GetAsync(actor,appId,sessionId,ct);
        if(limit is <1 or >100)throw MiniAppInput.Invalid("Giới hạn trang diagnostic từ 1 đến 100.");
        var filter=MiniAppSandboxDiagnosticCatalog.Filter(category);
        var query=db.MiniAppSandboxDiagnostics.AsNoTracking().Where(x=>x.SandboxSessionId==sessionId);
        if(filter is {} kind)query=query.Where(x=>x.Category==kind);
        if(cursor!=null)
        {
            if(!Guid.TryParseExact(cursor,"N",out var cursorId))throw MiniAppInput.Invalid("Cursor diagnostic không hợp lệ.");
            var anchor=await query.SingleOrDefaultAsync(x=>x.Id==cursorId,ct)??throw MiniAppInput.Invalid("Cursor diagnostic không còn hợp lệ.");
            // Session cap is 500; textual UUID ordering is portable across SQLite/PostgreSQL.
            var anchorId=anchor.Id.ToString();
            query=query.Where(x=>x.CreatedAt>anchor.CreatedAt || x.CreatedAt==anchor.CreatedAt && string.Compare(x.Id.ToString().ToLower(),anchorId)>0);
        }
        var rows=await query.OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id.ToString().ToLower()).Take(limit+1).ToArrayAsync(ct);
        var items=rows.Take(limit).ToArray();var next=rows.Length>limit?items[^1].Id.ToString("N"):null;
        var dropped=await db.MiniAppSandboxSessions.Where(x=>x.Id==sessionId).Select(x=>x.DroppedDiagnosticCount).SingleAsync(ct);
        return new(items.Select(MiniAppSandboxDiagnosticCatalog.Dto).ToArray(),next,dropped);
    }
}
