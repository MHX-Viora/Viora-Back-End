using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// Credential values are transient: only send Binding/Credential as host-only HttpOnly cookies.
public sealed record SandboxBrowserPreparation(string Nonce,string Binding,DateTime ExpiresAt);
public sealed record SandboxLaunchDto(string Grant,DateTime ExpiresAt);
public sealed record SandboxRuntimeCredential(string Credential,DateTime ExpiresAt);

public sealed class MiniAppSandboxGrantService(AppDbContext db,MiniAppSandboxSessionService sessions,TimeProvider clock,
    MiniAppSandboxOptions options,IDataProtectionProvider protection)
{
    private readonly ITimeLimitedDataProtector binding=protection.CreateProtector("ANKT.MiniAppSandbox.Binding.v1").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector executorRuntime=protection.CreateProtector("ANKT.MiniAppSandbox.ExecutorRuntime.v1").ToTimeLimitedDataProtector();
    private sealed record ExecutorRuntime(Guid Run,Guid Version,Guid Session,string Fingerprint);
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private string Audience=>new Uri(options.HostOrigin).GetLeftPart(UriPartial.Authority);
    public SandboxBrowserPreparation Prepare()
    {
        if(!options.Enabled)throw new DeveloperAuthException(503,"SANDBOX_DISABLED","Sandbox chưa được bật.");
        var nonce=Random(); var expiry=clock.GetUtcNow().AddMinutes(2);
        return new(nonce,binding.Protect(nonce,expiry),expiry.UtcDateTime);
    }

    public async Task<SandboxLaunchDto> IssueAsync(SandboxActor actor,string appId,Guid sessionId,string nonce,CancellationToken ct,Func<CancellationToken,Task>? guard=null,(Guid Id,Guid Version)? executor=null)
    {
        try{return await IssueCore(actor,appId,sessionId,nonce,ct,guard,executor);}
        catch(Exception error) when(MiniAppSandboxSessionService.SerializationConflict(error)){throw Conflict();}
    }

    private async Task<SandboxLaunchDto> IssueCore(SandboxActor actor,string appId,Guid sessionId,string nonce,CancellationToken ct,Func<CancellationToken,Task>? guard,(Guid Id,Guid Version)? executor)
    {
        if(!Proof(nonce))throw MiniAppInput.Invalid("Browser nonce không hợp lệ.");
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        if(guard!=null)await guard(ct);
        await sessions.RequireSessionAsync(actor,appId,sessionId,ct); var now=Now;
        var run=executor is {} expected?await ExecutorRunAsync(expected.Id,expected.Version,sessionId,ct):null;
        if(await db.MiniAppSandboxLaunchGrants.CountAsync(x=>x.SandboxSessionId==sessionId && x.ConsumedAt==null && x.ExpiresAt>now,ct)>=3)
            throw new DeveloperAuthException(429,"SANDBOX_LAUNCH_LIMIT","Có quá nhiều yêu cầu khởi chạy. Vui lòng chờ rồi thử lại.");
        var grant=Random(); var expiry=now.AddSeconds(options.GrantSeconds);
        if(run!=null&&expiry>run.DeadlineAt)expiry=run.DeadlineAt;
        db.MiniAppSandboxLaunchGrants.Add(new(){SandboxSessionId=sessionId,ExecutorRunId=run?.Id,ExecutorRunVersion=run?.Version,GrantHash=Hash(grant),BrowserNonceHash=Hash(nonce),Audience=Audience,ExpiresAt=expiry});
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(grant,expiry);
    }

    public async Task<SandboxRuntimeCredential> RedeemAsync(Guid sessionId,string grant,string nonce,string? browserBinding,CancellationToken ct)
    {
        try{return await RedeemCore(sessionId,grant,nonce,browserBinding,ct);}
        catch(Exception error) when(MiniAppSandboxSessionService.SerializationConflict(error)){throw Conflict();}
    }

    private async Task<SandboxRuntimeCredential> RedeemCore(Guid sessionId,string grant,string nonce,string? browserBinding,CancellationToken ct)
    {
        if(!Proof(grant)||!Proof(nonce)||browserBinding is not {Length:>0 and <=4096})throw Invalid();
        try
        {
            var expected=binding.Unprotect(browserBinding,out _);
            if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(nonce))))throw Invalid();
        }
        catch(CryptographicException){throw Invalid();}
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var hash=Hash(grant); var now=Now;
        var row=await db.MiniAppSandboxLaunchGrants.AsNoTracking().SingleOrDefaultAsync(x=>x.GrantHash==hash && x.SandboxSessionId==sessionId,ct);
        if(row==null || row.ConsumedAt!=null || row.ExpiresAt<=now || row.Audience!=Audience || row.BrowserNonceHash!=Hash(nonce))throw Invalid();
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sessionId,ct) ?? throw Invalid();
        var appId=await db.MiniApps.AsNoTracking().Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleOrDefaultAsync(ct) ?? throw Invalid();
        await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);
        var version=Guid.NewGuid();
        var consumed=await db.MiniAppSandboxLaunchGrants.Where(x=>x.Id==row.Id && x.Version==row.Version && x.ConsumedAt==null && x.ExpiresAt>now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now).SetProperty(x=>x.Version,version),ct);
        if(consumed!=1)throw Invalid();
        if(row.ExecutorRunId is {} runId){
            var run=await ExecutorRunAsync(runId,row.ExecutorRunVersion??Guid.Empty,sessionId,ct);
            if(run.ConfigurationFingerprint!=session.ConfigurationFingerprint)throw Invalid();
            var expiry=run.DeadlineAt<session.ExpiresAt?run.DeadlineAt:session.ExpiresAt;
            var value=executorRuntime.Protect(JsonSerializer.Serialize(new ExecutorRuntime(run.Id,run.Version,session.Id,run.ConfigurationFingerprint)),new DateTimeOffset(DateTime.SpecifyKind(expiry,DateTimeKind.Utc)));
            await transaction.CommitAsync(ct);return new("e1."+value,DateTime.SpecifyKind(expiry,DateTimeKind.Utc));
        }
        var credential=Random(); var credentialHash=Hash(credential);
        var changed=await db.MiniAppSandboxSessions.Where(x=>x.Id==session.Id && x.Version==session.Version && x.RevokedAt==null && x.ExpiresAt>now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RuntimeCredentialHash,credentialHash).SetProperty(x=>x.LastActivityAt,now).SetProperty(x=>x.Version,version),ct);
        if(changed!=1)throw Invalid();
        await transaction.CommitAsync(ct); return new(credential,DateTime.SpecifyKind(session.ExpiresAt,DateTimeKind.Utc));
    }

    public async Task<MiniAppSandboxSession> RequireRuntimeAsync(string? credential,CancellationToken ct)
    {
        return (await RuntimeAsync(credential,ct)).Session;
    }
    private async Task<(MiniAppSandboxSession Session,Guid? RunId)> RuntimeAsync(string? credential,CancellationToken ct)
    {
        ExecutorRuntime? claim=null;DateTimeOffset expiry=default;
        if(credential is {Length:>3 and <=2048}&&credential.StartsWith("e1.",StringComparison.Ordinal)){
            try{claim=JsonSerializer.Deserialize<ExecutorRuntime>(executorRuntime.Unprotect(credential[3..],out expiry))??throw Invalid();}catch(Exception e) when(e is CryptographicException or JsonException){throw Invalid();}
            if(expiry.UtcDateTime<=Now)throw Invalid();
        }else if(!Proof(credential))throw Invalid();
        var hash=claim==null?Hash(credential!):null;
        var query=db.MiniAppSandboxSessions.AsNoTracking();
        var session=(claim==null?await query.SingleOrDefaultAsync(x=>x.RuntimeCredentialHash==hash,ct):await query.SingleOrDefaultAsync(x=>x.Id==claim.Session,ct))??throw Invalid();
        var appId=await db.MiniApps.AsNoTracking().Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleOrDefaultAsync(ct) ?? throw Invalid();
        session=await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);
        if(claim!=null){
            var run=await ExecutorRunAsync(claim.Run,claim.Version,session.Id,ct);
            if(claim.Fingerprint!=session.ConfigurationFingerprint||claim.Fingerprint!=run.ConfigurationFingerprint||expiry.UtcDateTime>run.DeadlineAt||expiry.UtcDateTime>session.ExpiresAt)throw Invalid();
            session.ExpiresAt=expiry.UtcDateTime;
        }
        return (session,claim?.Run);
    }
    private async Task<MiniAppSandboxTestRun> ExecutorRunAsync(Guid id,Guid version,Guid session,CancellationToken ct)=>
        await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Version==version&&x.SandboxSessionId==session&&x.Status==SandboxTestRunStatus.Running&&x.DeadlineAt>Now,ct)??throw Invalid();
    public async Task CloseRuntimeAsync(string? credential,MiniAppSandboxRunCoordinator runs,CancellationToken ct)
    {
        var scope=await RuntimeAsync(credential,ct);var session=scope.Session;
        var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
        if(scope.RunId is {} run)await runs.CancelAsync(MiniAppSandboxSessionService.Actor(session),appId,run,ct);
        else await sessions.RevokeAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);
    }

    private static bool Proof(string? value)=>value is {Length:43} && value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
    private static string Random()=>WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DeveloperAuthException Invalid()=>new(403,"SANDBOX_LAUNCH_INVALID","Yêu cầu khởi chạy Sandbox không hợp lệ hoặc đã hết hạn.");
    private static DeveloperAuthException Conflict()=>new(409,"SANDBOX_CONFLICT","Phiên vừa thay đổi. Vui lòng tải lại trước khi thử lại.");
}
