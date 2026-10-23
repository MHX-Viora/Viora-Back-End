using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// No public ingestion API. Call only after OpenIddict has validated/generated the actual protocol artifact.
public sealed class MiniAppSandboxProtocolService(AppDbContext db,MiniAppSandboxSessionService sessions,
    MiniAppSandboxOptions options,TimeProvider clock)
{
    public const string RunClaim="oi_ankt_sandbox_run",VersionClaim="oi_ankt_sandbox_version";
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    public async Task BindAsync(ClaimsPrincipal principal,string? marker,string clientKey,CancellationToken ct)
    {
        principal.SetClaim(RunClaim,(string?)null);principal.SetClaim(VersionClaim,(string?)null);
        if(!Guid.TryParseExact(marker,"D",out var id))return;
        var run=await CurrentAsync(id,null,clientKey,ct);if(run==null)return;
        principal.SetClaim(RunClaim,id.ToString());principal.SetClaim(VersionClaim,run.Version.ToString());
    }
    private async Task<MiniAppSandboxTestRun?> CurrentAsync(Guid id,Guid? version,string clientKey,CancellationToken ct)
    {
        if(!options.Enabled)return null;
        var now=Now;
        var run=await db.MiniAppSandboxTestRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Status==SandboxTestRunStatus.Running&&
            x.Environment==MiniAppOAuthEnvironment.Sandbox&&x.DeadlineAt>now,ct);
        if(run==null||version!=null&&run.Version!=version)return null;
        var session=await db.MiniAppSandboxSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==run.SandboxSessionId,ct);
        if(session==null||session.ConfigurationFingerprint!=run.ConfigurationFingerprint)return null;
        var client=await db.MiniAppOAuthClients.AsNoTracking().Include(x=>x.MiniApp).SingleOrDefaultAsync(x=>x.Id==session.OAuthClientId,ct);
        if(client==null||client.ClientKey!=clientKey||client.Environment!=MiniAppOAuthEnvironment.Sandbox||!client.MiniApp.SsoEnabled)return null;
        try{await sessions.RequireSessionAsync(MiniAppSandboxSessionService.Actor(session),client.MiniApp.AppId,session.Id,ct);}
        catch(DeveloperAuthException){return null;}
        return run;
    }
    public async Task RecordAsync(ClaimsPrincipal principal,SandboxProtocolStage stage,string? internalTokenId,CancellationToken ct)
    {
        if(!Guid.TryParseExact(principal.GetClaim(RunClaim),"D",out var id)||!Guid.TryParseExact(principal.GetClaim(VersionClaim),"D",out var version))return;
        var presenters=principal.GetPresenters();var authorization=principal.GetAuthorizationId();
        if(presenters.Length!=1||!Identifier(authorization)||!Identifier(internalTokenId)||stage==SandboxProtocolStage.UserInfo)return;
        await StoreAsync(id,version,presenters[0],stage,authorization!,internalTokenId!,ct);
    }
    public async Task UserInfoAsync(ClaimsPrincipal validatedAccessPrincipal,CancellationToken ct)
    {
        var token=validatedAccessPrincipal.GetTokenId();var authorization=validatedAccessPrincipal.GetAuthorizationId();
        var presenters=validatedAccessPrincipal.GetPresenters();
        if(!Identifier(token)||!Identifier(authorization)||presenters.Length!=1)return;
        // Private code claims are deliberately excluded from access tokens. Correlate by actual internal token row instead.
        var evidence=await db.MiniAppSandboxProtocolEvidence.AsNoTracking().Where(x=>x.TokenId==token&&x.AuthorizationId==authorization&&
            x.Stage==SandboxProtocolStage.Token&&x.Outcome==SandboxProtocolOutcome.TokenIssued).Take(2).ToArrayAsync(ct);
        if(evidence.Length!=1)return;
        var run=await CurrentAsync(evidence[0].TestRunId,null,presenters[0],ct);if(run==null)return;
        await StoreAsync(run.Id,run.Version,presenters[0],SandboxProtocolStage.UserInfo,authorization!,token!,ct);
    }
    private async Task StoreAsync(Guid id,Guid version,string clientKey,SandboxProtocolStage stage,string authorization,string token,CancellationToken ct)
    {
        var key=stage switch{SandboxProtocolStage.Authorize=>SandboxCheckKey.Consent,SandboxProtocolStage.Token=>SandboxCheckKey.Token,_=>SandboxCheckKey.UserInfo};
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var run=await CurrentAsync(id,version,clientKey,ct);if(run==null)return;
        if(!await db.MiniAppSandboxTestResults.AnyAsync(x=>x.TestRunId==id&&x.TestKey==key,ct)||
            await db.MiniAppSandboxProtocolEvidence.CountAsync(x=>x.TestRunId==id,ct)>=32)return;
        var eventKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}|{version:D}|{stage}|{authorization}|{token}")));
        if(await db.MiniAppSandboxProtocolEvidence.AnyAsync(x=>x.EventKey==eventKey,ct))return;
        db.MiniAppSandboxProtocolEvidence.Add(new(){TestRunId=id,TestKey=key,Stage=stage,AuthorizationId=authorization,TokenId=token,
            EventKey=eventKey,CreatedAt=Now,Outcome=stage switch{
                SandboxProtocolStage.Authorize=>SandboxProtocolOutcome.Authorized,SandboxProtocolStage.Token=>SandboxProtocolOutcome.TokenIssued,_=>SandboxProtocolOutcome.UserInfoValidated}});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static bool Identifier(string? value)=>value is {Length:>0 and <=100}&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
}
