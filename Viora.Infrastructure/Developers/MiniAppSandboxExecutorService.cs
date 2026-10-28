using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxExecutorService(AppDbContext db,MiniAppSandboxSessionService sessions,
    MiniAppSandboxOptions options,TimeProvider clock,IDataProtectionProvider protection)
{
    private sealed record Challenge(Guid Nonce,Guid Run,Guid Version,Guid Session,string Fingerprint,SandboxCheckKey Key,DateTime ExpiresAt);
    private readonly IDataProtector protector=protection.CreateProtector("ANKT.MiniAppSandbox.Executor.v1");
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    public bool Enabled=>options.Enabled&&options.ExecutorKey.Length>0;
    public bool Authenticate(string? authorization)=>Enabled&&authorization is {Length:>16 and <=256}&&authorization.StartsWith("SandboxExecutor ",StringComparison.Ordinal)&&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(authorization[16..])),SHA256.HashData(Encoding.UTF8.GetBytes(options.ExecutorKey)));
    private static DeveloperAuthException Denied()=>new(403,"SANDBOX_EXECUTOR_PROOF_INVALID","Bằng chứng executor không hợp lệ hoặc đã hết hạn.");
    private static (SandboxDiagnosticCategory Category,SandboxMessageCode Passed,SandboxMessageCode Failed) Codes(SandboxCheckKey key)=>key switch{
        SandboxCheckKey.Runtime=>(SandboxDiagnosticCategory.Runtime,SandboxMessageCode.RuntimeReady,SandboxMessageCode.RuntimeError),
        SandboxCheckKey.Bridge=>(SandboxDiagnosticCategory.Bridge,SandboxMessageCode.BridgeReady,SandboxMessageCode.BridgeError),_=>throw Denied()};
    private async Task<(MiniAppSandboxTestRun Run,MiniAppSandboxSession Session,string AppId)> Current(Guid id,Guid? version,CancellationToken ct)
    {
        var now=Now;
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Status==SandboxTestRunStatus.Running&&x.DeadlineAt>now&&x.Environment==MiniAppOAuthEnvironment.Sandbox,ct)??throw Denied();
        if(version!=null&&run.Version!=version)throw Denied();
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleAsync(x=>x.Id==run.SandboxSessionId,ct);
        var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
        await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);
        if(session.ConfigurationFingerprint!=run.ConfigurationFingerprint)throw Denied();return(run,session,appId);
    }
    public async Task<SandboxExecutorJob[]> JobsAsync(CancellationToken ct)
    {
        if(!Enabled)return [];
        var now=Now;var ids=await db.MiniAppSandboxTestRuns.Where(x=>x.Status==SandboxTestRunStatus.Running&&x.DeadlineAt>now).OrderBy(x=>x.CreatedAt).Take(8).Select(x=>x.Id).ToArrayAsync(ct);
        var jobs=new List<SandboxExecutorJob>();
        foreach(var id in ids){
            try{
                var (run,session,appId)=await Current(id,null,ct);var checks=new List<SandboxExecutorCheck>();
                foreach(var key in new[]{SandboxCheckKey.Runtime,SandboxCheckKey.Bridge}){
                    var codes=Codes(key);
                    if(!await db.MiniAppSandboxTestResults.AnyAsync(x=>x.TestRunId==id&&x.TestKey==key&&x.MessageCode==SandboxMessageCode.CheckPending,ct)||
                        await db.MiniAppSandboxDiagnostics.AnyAsync(x=>x.TestRunId==id&&x.EvidenceSource==SandboxEvidenceSource.TrustedBrowser&&x.Category==codes.Category,ct))continue;
                    var expiry=new[]{Now.AddSeconds(60),run.DeadlineAt,session.ExpiresAt}.Min();
                    var claim=new Challenge(Guid.NewGuid(),id,run.Version,session.Id,run.ConfigurationFingerprint,key,expiry);
                    checks.Add(new(key.ToString().ToLowerInvariant(),protector.Protect(JsonSerializer.Serialize(claim)),DateTime.SpecifyKind(expiry,DateTimeKind.Utc)));
                }
                if(checks.Count>0)jobs.Add(new(id,session.Id,appId,run.ConfigurationFingerprint,session.WebsiteUrl,session.PartnerOrigin,options.HostOrigin,options.PortalOrigin,
                    DateTime.SpecifyKind(run.DeadlineAt,DateTimeKind.Utc),checks.ToArray()));
            }catch(DeveloperAuthException){/* A stale session does not prevent independent jobs. */}
        }
        return jobs.ToArray();
    }
    private Challenge Verify(string? value,string payload,string? signature)
    {
        if(!Enabled||value is not {Length:>0 and <=4096}||signature is not {Length:64})throw Denied();
        try{
            var expected=HMACSHA256.HashData(Convert.FromBase64String(options.ExecutorKey),Encoding.UTF8.GetBytes(value+"\n"+payload));
            if(!CryptographicOperations.FixedTimeEquals(expected,Convert.FromHexString(signature)))throw Denied();
            var claim=JsonSerializer.Deserialize<Challenge>(protector.Unprotect(value))??throw Denied();
            if(claim.Nonce==Guid.Empty||claim.ExpiresAt<=Now||claim.ExpiresAt>Now.AddSeconds(61))throw Denied();
            Codes(claim.Key);return claim;
        }catch(Exception error) when(error is not DeveloperAuthException){throw Denied();}
    }
    private async Task<MiniAppSandboxSession> Require(Challenge claim,CancellationToken ct)
    {
        var (run,session,_)=await Current(claim.Run,claim.Version,ct);
        if(session.Id!=claim.Session||run.ConfigurationFingerprint!=claim.Fingerprint||claim.ExpiresAt>run.DeadlineAt||claim.ExpiresAt>session.ExpiresAt)throw Denied();
        return session;
    }
    public async Task ReportAsync(SandboxExecutorReport report,string? signature,CancellationToken ct)
    {
        if(report.Status is not ("Passed" or "Failed"))throw Denied();
        var claim=Verify(report.Challenge,report.Status,signature);var codes=Codes(claim.Key);
        try{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            await Require(claim,ct);
            if(!await db.MiniAppSandboxTestResults.AnyAsync(x=>x.TestRunId==claim.Run&&x.TestKey==claim.Key&&x.MessageCode==SandboxMessageCode.CheckPending,ct)||
                await db.MiniAppSandboxDiagnostics.AnyAsync(x=>x.Id==claim.Nonce||x.TestRunId==claim.Run&&x.EvidenceSource==SandboxEvidenceSource.TrustedBrowser&&x.Category==codes.Category,ct))throw Denied();
            db.MiniAppSandboxDiagnostics.Add(new(){Id=claim.Nonce,TestRunId=claim.Run,SandboxSessionId=claim.Session,EvidenceSource=SandboxEvidenceSource.TrustedBrowser,
                Category=codes.Category,Code=report.Status=="Passed"?codes.Passed:codes.Failed,Level=report.Status=="Passed"?SandboxDiagnosticLevel.Info:SandboxDiagnosticLevel.Error});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }catch(Exception error) when(MiniAppSandboxSessionService.SerializationConflict(error)||error is DbUpdateException){throw Denied();}
    }
    public async Task<SandboxLaunchDto> LaunchAsync(SandboxExecutorLaunch request,string? signature,MiniAppSandboxGrantService grants,CancellationToken ct)
    {
        var claim=Verify(request.Challenge,request.BrowserNonce,signature);var session=await Require(claim,ct);
        var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
        return await grants.IssueAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,request.BrowserNonce,ct,
            async token=>{if(claim.ExpiresAt<=Now)throw Denied();await Require(claim,token);},(claim.Run,claim.Version));
    }
}
