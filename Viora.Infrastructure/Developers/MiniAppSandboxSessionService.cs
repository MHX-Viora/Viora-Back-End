using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxSessionService(AppDbContext db, MiniAppSandboxAccess access, TimeProvider clock, MiniAppSandboxOptions options, MiniAppSandboxRunCancellation? runCancellation=null)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    public async Task<SandboxSessionDto> CreateAsync(SandboxActor actor,string appId,CancellationToken ct)
    {
        try{return await CreateCore(actor,appId,ct);}
        catch(Exception error) when(SerializationConflict(error))
        {throw new DeveloperAuthException(409,"SANDBOX_CONFLICT","Phiên vừa thay đổi. Vui lòng tải lại trước khi thử lại.");}
    }

    private async Task<SandboxSessionDto> CreateCore(SandboxActor actor,string appId,CancellationToken ct)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var app=await access.RequireAsync(actor,appId,ct); var (client,website,origin,fingerprint)=await Binding(app,ct); var now=Now;
        var active=db.MiniAppSandboxSessions.Where(x=>x.RevokedAt==null && x.ExpiresAt>now &&
            (actor.Kind==SandboxActorKind.Developer ? x.ActorDeveloperAccountId==actor.Id : x.ActorAdminAccountId==actor.Id));
        if (await active.CountAsync(ct)>=options.ActiveSessionsPerActor || await active.AnyAsync(x=>x.MiniAppId==app.Id,ct))
            throw new DeveloperAuthException(409,"SANDBOX_SESSION_LIMIT","Dừng phiên Sandbox đang chạy trước khi mở phiên mới.");
        var session=new MiniAppSandboxSession {MiniAppId=app.Id,DeveloperAccountId=app.DeveloperAccountId,OAuthClientId=client.Id,
            ActorType=actor.Kind.ToString(),ActorDeveloperAccountId=actor.Kind==SandboxActorKind.Developer?actor.Id:null,
            ActorAdminAccountId=actor.Kind==SandboxActorKind.Admin?actor.Id:null,
            SourceDeveloperSessionId=actor.Kind==SandboxActorKind.Developer?actor.SourceSessionId:null,
            SourceSocialSessionId=actor.Kind==SandboxActorKind.Admin?actor.SourceSessionId:null,
            WebsiteUrl=website,PartnerOrigin=origin,ConfigurationFingerprint=fingerprint,ExpiresAt=now.AddMinutes(options.SessionMinutes),LastActivityAt=now};
        db.MiniAppSandboxSessions.Add(session); Audit(actor,app.Id,"sandbox.create",session.Id);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return Dto(session,appId);
    }

    public async Task<MiniAppSandboxSession> RequireSessionAsync(SandboxActor actor,string appId,Guid sessionId,CancellationToken ct)
    {
        var app=await access.RequireAsync(actor,appId,ct); var session=await Owned(actor,app.Id,sessionId,ct);
        var source=Actor(session);
        if(source.SourceSessionId!=actor.SourceSessionId) await access.RequireAsync(source,appId,ct);
        if (session.RevokedAt!=null || session.ExpiresAt<=Now)
            throw new DeveloperAuthException(410,"SANDBOX_SESSION_ENDED","Phiên Sandbox đã kết thúc. Vui lòng khởi chạy lại.");
        var (client,_,_,fingerprint)=await Binding(app,ct);
        if (session.Environment!=MiniAppOAuthEnvironment.Sandbox || session.OAuthClientId!=client.Id || session.ConfigurationFingerprint!=fingerprint)
            throw new DeveloperAuthException(409,"SANDBOX_CONFIGURATION_CHANGED","Cấu hình đã thay đổi. Vui lòng tạo phiên Sandbox mới.");
        return session;
    }

    public async Task<SandboxSessionDto> GetAsync(SandboxActor actor,string appId,Guid sessionId,CancellationToken ct)
    {
        var app=await access.RequireAsync(actor,appId,ct,true);
        return Dto(await Owned(actor,app.Id,sessionId,ct),appId);
    }

    public async Task RevokeAsync(SandboxActor actor,string appId,Guid sessionId,CancellationToken ct)
    {
        var app=await access.RequireAsync(actor,appId,ct,true); await Owned(actor,app.Id,sessionId,ct);
        var now=Now; var version=Guid.NewGuid();
        await using(var transaction=await db.Database.BeginTransactionAsync(ct))
        {
            var changed=await db.MiniAppSandboxSessions.Where(x=>x.Id==sessionId && x.RevokedAt==null)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,now).SetProperty(x=>x.RuntimeCredentialHash,(string?)null).SetProperty(x=>x.Version,version),ct);
            await db.MiniAppSandboxLaunchGrants.Where(x=>x.SandboxSessionId==sessionId && x.ConsumedAt==null)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now).SetProperty(x=>x.Version,version),ct);
            var activeRuns=db.MiniAppSandboxTestRuns.Where(x=>x.SandboxSessionId==sessionId&&(x.Status==SandboxTestRunStatus.Queued||x.Status==SandboxTestRunStatus.Running));
            var runIds=await activeRuns.Select(x=>x.Id).ToArrayAsync(ct);
            await activeRuns
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxTestRunStatus.Cancelled).SetProperty(x=>x.SummaryCode,SandboxMessageCode.RunCancelled)
                    .SetProperty(x=>x.CompletedAt,x=>x.StartedAt!=null&&x.StartedAt>now?x.StartedAt:x.CreatedAt>now?x.CreatedAt:now).SetProperty(x=>x.Version,version),ct);
            await db.MiniAppSandboxTestResults.Where(x=>runIds.Contains(x.TestRunId)&&x.MessageCode==SandboxMessageCode.CheckPending)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxCheckStatus.Skipped).SetProperty(x=>x.MessageCode,SandboxMessageCode.MissingEvidence).SetProperty(x=>x.Version,version),ct);
            if(changed!=0) {Audit(actor,app.Id,"sandbox.revoke",sessionId); await db.SaveChangesAsync(ct);}
            await transaction.CommitAsync(ct);
        }
        runCancellation?.CancelSession(sessionId);
    }

    private async Task<MiniAppSandboxSession> Owned(SandboxActor actor,Guid appId,Guid id,CancellationToken ct) =>
        await db.MiniAppSandboxSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.MiniAppId==appId &&
            (actor.Kind==SandboxActorKind.Developer ? x.ActorDeveloperAccountId==actor.Id : x.ActorAdminAccountId==actor.Id),ct)
        ?? throw MiniAppSandboxAccess.NotFound();

    public static SandboxActor Actor(MiniAppSandboxSession session)=>session.ActorType switch
    {
        "Developer" when session.ActorDeveloperAccountId!=null && session.SourceDeveloperSessionId!=null =>
            new(SandboxActorKind.Developer,session.ActorDeveloperAccountId.Value,session.SourceDeveloperSessionId.Value),
        "Admin" when session.ActorAdminAccountId!=null && session.SourceSocialSessionId!=null =>
            new(SandboxActorKind.Admin,session.ActorAdminAccountId.Value,session.SourceSocialSessionId.Value),
        _=>throw MiniAppSandboxAccess.NotFound()
    };

    internal static bool SerializationConflict(Exception error)
    {
        for(Exception? current=error;current!=null;current=current.InnerException)
            if(current is Npgsql.PostgresException {SqlState:"40001" or "40P01"})return true;
        return false;
    }

    private async Task<(MiniAppOAuthClient Client,string Website,string Origin,string Fingerprint)> Binding(MiniApp app,CancellationToken ct)
    {
        var client=await db.MiniAppOAuthClients.AsNoTracking().SingleOrDefaultAsync(x=>x.MiniAppId==app.Id && x.Environment==MiniAppOAuthEnvironment.Sandbox && x.RevokedAt==null,ct);
        var proof=await db.MiniAppDomainVerifications.AsNoTracking().SingleOrDefaultAsync(x=>x.MiniAppId==app.Id && x.RevokedAt==null,ct);
        var website=PublicWebAddress.Normalize(app.WebsiteUrl); var uri=new Uri(website); var origin=uri.GetLeftPart(UriPartial.Authority);
        if(client is not {SandboxEnabled:true} || client.ClientType is not ("Public" or "Confidential") ||
            proof is not {Status:MiniAppDomainStatus.Verified} || proof.ExpiresAt<=Now || proof.Hostname!=uri.IdnHost.ToLowerInvariant() ||
            new[]{options.PortalOrigin,options.HostOrigin,options.Issuer}.Any(value=>new Uri(value).GetLeftPart(UriPartial.Authority)==origin))
            throw new DeveloperAuthException(403,"SANDBOX_CONFIGURATION_UNAVAILABLE","Cần Sandbox client hợp lệ và domain đã xác minh.");
        var redirects=await db.MiniAppRedirectUris.AsNoTracking().Where(x=>x.MiniAppId==app.Id && x.Environment==MiniAppOAuthEnvironment.Sandbox)
            .OrderBy(x=>x.RedirectUri).Select(x=>x.RedirectUri).ToArrayAsync(ct);
        if(app.SsoEnabled)
        {
            SsoClientPolicy.EnsureEligible(client,app,proof,client.AllowedScopes.Split(' ',StringSplitOptions.RemoveEmptyEntries),"Sandbox",Now);
            if(redirects.Length==0 || redirects.Any(value=>!Uri.TryCreate(value,UriKind.Absolute,out var callback)||callback.Scheme!="https"||callback.UserInfo.Length!=0||callback.Query.Length!=0||callback.Fragment.Length!=0))
                throw new DeveloperAuthException(403,"SANDBOX_SSO_REDIRECT_REQUIRED","Cần callback HTTPS hợp lệ cho SSO Sandbox.");
        }
        var fingerprint=Fingerprint(app,client,proof,website,redirects);
        return (client,website,origin,fingerprint);
    }

    internal static string Fingerprint(MiniApp app,MiniAppOAuthClient? client,MiniAppDomainVerification? proof,string website,string[] redirects)=>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{
            app.Version,ClientVersion=client?.Version,ProofVersion=proof?.Version,ProofExpiry=proof?.ExpiresAt.Ticks,website,app.SsoEnabled,client?.ClientKey,client?.AllowedScopes,redirects}))));

    private void Audit(SandboxActor actor,Guid app,string action,Guid session)
    {
        if(actor.Kind==SandboxActorKind.Developer)
            db.MiniAppAuditLogs.Add(new(){MiniAppId=app,ActorDeveloperId=actor.Id,Action=action,Metadata=JsonSerializer.Serialize(new{sessionId=session})});
        // Admin actor identity is retained on the session; never attribute it to the owner.
    }
    public static SandboxSessionDto Dto(MiniAppSandboxSession session,string appId)=>new(session.Id,appId,"Sandbox",DateTime.SpecifyKind(session.CreatedAt,DateTimeKind.Utc),
        DateTime.SpecifyKind(session.ExpiresAt,DateTimeKind.Utc),session.RevokedAt,session.LastActivityAt,session.ConfigurationFingerprint);
}
