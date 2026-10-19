using System.Data;
using System.Security.Cryptography;
using System.Text;
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
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private string Audience=>new Uri(options.HostOrigin).GetLeftPart(UriPartial.Authority);
    public SandboxBrowserPreparation Prepare()
    {
        if(!options.Enabled)throw new DeveloperAuthException(503,"SANDBOX_DISABLED","Sandbox chưa được bật.");
        var nonce=Random(); var expiry=clock.GetUtcNow().AddMinutes(2);
        return new(nonce,binding.Protect(nonce,expiry),expiry.UtcDateTime);
    }

    public async Task<SandboxLaunchDto> IssueAsync(SandboxActor actor,string appId,Guid sessionId,string nonce,CancellationToken ct)
    {
        try{return await IssueCore(actor,appId,sessionId,nonce,ct);}
        catch(Exception error) when(MiniAppSandboxSessionService.SerializationConflict(error)){throw Conflict();}
    }

    private async Task<SandboxLaunchDto> IssueCore(SandboxActor actor,string appId,Guid sessionId,string nonce,CancellationToken ct)
    {
        if(!Proof(nonce))throw MiniAppInput.Invalid("Browser nonce không hợp lệ.");
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await sessions.RequireSessionAsync(actor,appId,sessionId,ct); var now=Now;
        if(await db.MiniAppSandboxLaunchGrants.CountAsync(x=>x.SandboxSessionId==sessionId && x.ConsumedAt==null && x.ExpiresAt>now,ct)>=3)
            throw new DeveloperAuthException(429,"SANDBOX_LAUNCH_LIMIT","Có quá nhiều yêu cầu khởi chạy. Vui lòng chờ rồi thử lại.");
        var grant=Random(); var expiry=now.AddSeconds(options.GrantSeconds);
        db.MiniAppSandboxLaunchGrants.Add(new(){SandboxSessionId=sessionId,GrantHash=Hash(grant),BrowserNonceHash=Hash(nonce),Audience=Audience,ExpiresAt=expiry});
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
        var credential=Random(); var credentialHash=Hash(credential);
        var changed=await db.MiniAppSandboxSessions.Where(x=>x.Id==session.Id && x.Version==session.Version && x.RevokedAt==null && x.ExpiresAt>now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RuntimeCredentialHash,credentialHash).SetProperty(x=>x.LastActivityAt,now).SetProperty(x=>x.Version,version),ct);
        if(changed!=1)throw Invalid();
        await transaction.CommitAsync(ct); return new(credential,DateTime.SpecifyKind(session.ExpiresAt,DateTimeKind.Utc));
    }

    public async Task<MiniAppSandboxSession> RequireRuntimeAsync(string? credential,CancellationToken ct)
    {
        if(!Proof(credential))throw Invalid(); var hash=Hash(credential!);
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.RuntimeCredentialHash==hash,ct) ?? throw Invalid();
        var appId=await db.MiniApps.AsNoTracking().Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleOrDefaultAsync(ct) ?? throw Invalid();
        return await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);
    }

    private static bool Proof(string? value)=>value is {Length:43} && value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
    private static string Random()=>WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DeveloperAuthException Invalid()=>new(403,"SANDBOX_LAUNCH_INVALID","Yêu cầu khởi chạy Sandbox không hợp lệ hoặc đã hết hạn.");
    private static DeveloperAuthException Conflict()=>new(409,"SANDBOX_CONFLICT","Phiên vừa thay đổi. Vui lòng tải lại trước khi thử lại.");
}
