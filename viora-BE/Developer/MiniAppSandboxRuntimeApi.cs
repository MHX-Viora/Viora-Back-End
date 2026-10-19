using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

namespace viora_BE.Developer;

public static class MiniAppSandboxRuntimeApi
{
    internal const string BindingCookie="__Host-AnktMiniAppSandboxBinding";
    internal const string RuntimeCookie="__Host-AnktMiniAppSandbox";

    public static void UseBoundary(WebApplication app)
    {
        var options=app.Services.GetRequiredService<MiniAppSandboxOptions>(); if(!options.Enabled)return;
        var host=new Uri(options.HostOrigin);
        app.Use(async(http,next)=>
        {
            // Hostname isolation, including requests made on a wrong port or scheme.
            if(!string.Equals(http.Request.Host.Host,host.Host,StringComparison.OrdinalIgnoreCase)){await next();return;}
            if(http.Request.Scheme!="https" || !string.Equals(http.Request.Host.Value,host.Authority,StringComparison.OrdinalIgnoreCase) ||
                !(http.Request.Path.StartsWithSegments("/sandbox-runtime") || http.Request.Path.StartsWithSegments("/sandbox-host")))
            {http.Response.StatusCode=404;http.Response.Headers.CacheControl="no-store";return;}
            http.User=new ClaimsPrincipal(new ClaimsIdentity()); await next();
        });
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/sandbox-runtime").RequireRateLimiting("mini-app-sandbox-runtime")
            .AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>();
        group.AddEndpointFilter(async(context,next)=>
        {
            var http=context.HttpContext; var options=http.RequestServices.GetRequiredService<MiniAppSandboxOptions>();
            http.Response.Headers.CacheControl="no-store"; http.Response.Headers["Referrer-Policy"]="no-referrer";
            http.Response.Headers["X-Content-Type-Options"]="nosniff";
            if(!options.Enabled)return Results.StatusCode(503);
            var host=new Uri(options.HostOrigin);
            if(http.Request.Scheme!="https" || !string.Equals(http.Request.Host.Value,host.Authority,StringComparison.OrdinalIgnoreCase))return Results.NotFound();
            var origin=http.Request.Headers.Origin;
            if((origin.Count>0 && (origin.Count!=1 || origin.ToString()!=host.GetLeftPart(UriPartial.Authority))) ||
                (http.Request.Method!="GET" && origin.Count!=1))return Results.StatusCode(403);
            return await next(context);
        });
        group.MapPost("/prepare",(HttpContext http,MiniAppSandboxGrantService service)=>
        {
            var prepared=service.Prepare(); http.Response.Cookies.Append(BindingCookie,prepared.Binding,Cookie(prepared.ExpiresAt));
            return Results.Ok(new{browserNonce=prepared.Nonce,expiresAt=prepared.ExpiresAt});
        });
        group.MapPost("/redeem",async(HttpContext http,MiniAppSandboxGrantService service,CancellationToken ct)=>
        {
            var body=await MiniAppSandboxApi.ReadRequest<SandboxRedeemRequest>(http,ct);
            var result=await service.RedeemAsync(body.SessionId,body.Grant,body.BrowserNonce,UniqueCookie(http,BindingCookie),ct);
            http.Response.Cookies.Append(RuntimeCookie,result.Credential,Cookie(result.ExpiresAt));
            http.Response.Cookies.Delete(BindingCookie,Cookie(DateTime.UtcNow));
            return Results.Ok(new{sessionId=body.SessionId,environment="Sandbox",expiresAt=result.ExpiresAt});
        });
        group.MapGet("/context",async(HttpContext http,MiniAppSandboxGrantService service,AppDbContext db,CancellationToken ct)=>
        {
            var session=await service.RequireRuntimeAsync(UniqueCookie(http,RuntimeCookie),ct);
            var app=await db.MiniApps.AsNoTracking().Where(x=>x.Id==session.MiniAppId).Select(x=>new{x.AppId,x.Name,x.LogoUrl,x.SsoEnabled}).SingleAsync(ct);
            return Results.Ok(new{sessionId=session.Id,appId=app.AppId,name=app.Name,logoUrl=app.LogoUrl,environment="Sandbox",websiteUrl=session.WebsiteUrl,
                partnerOrigin=session.PartnerOrigin,ssoEnabled=app.SsoEnabled,expiresAt=DateTime.SpecifyKind(session.ExpiresAt,DateTimeKind.Utc),configurationFingerprint=session.ConfigurationFingerprint});
        });
        group.MapPost("/heartbeat",async(HttpContext http,MiniAppSandboxGrantService service,AppDbContext db,TimeProvider clock,CancellationToken ct)=>
        {
            var session=await service.RequireRuntimeAsync(UniqueCookie(http,RuntimeCookie),ct); var now=clock.GetUtcNow().UtcDateTime;
            var cutoff=now.AddSeconds(-15); var version=Guid.NewGuid();
            await db.MiniAppSandboxSessions.Where(x=>x.Id==session.Id && x.RevokedAt==null && x.ExpiresAt>now && x.LastActivityAt<=cutoff)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastActivityAt,now).SetProperty(x=>x.Version,version),ct);
            return Results.Ok(new{environment="Sandbox",expiresAt=DateTime.SpecifyKind(session.ExpiresAt,DateTimeKind.Utc)});
        });
        foreach(var (path,source) in new[]{("sdk-observations",Viora.Domain.Developers.SandboxEvidenceSource.Sdk),("host-observations",Viora.Domain.Developers.SandboxEvidenceSource.Host)})
            group.MapPost("/"+path,async(HttpContext http,MiniAppSandboxGrantService grants,MiniAppSandboxDiagnosticService diagnostics,CancellationToken ct)=>
            {
                var session=await grants.RequireRuntimeAsync(UniqueCookie(http,RuntimeCookie),ct);
                var request=await MiniAppSandboxApi.ReadRequest<SandboxDiagnosticBatch>(http,ct,32768);
                return Results.Ok(await diagnostics.IngestAsync(session.Id,source,request,ct));
            });
        group.MapPost("/close",async(HttpContext http,MiniAppSandboxGrantService grants,MiniAppSandboxSessionService sessions,AppDbContext db,CancellationToken ct)=>
        {
            try
            {
                var session=await grants.RequireRuntimeAsync(UniqueCookie(http,RuntimeCookie),ct);
                var appId=await db.MiniApps.Where(x=>x.Id==session.MiniAppId).Select(x=>x.AppId).SingleAsync(ct);
                await sessions.RevokeAsync(MiniAppSandboxSessionService.Actor(session),appId,session.Id,ct);
            }
            catch(DeveloperAuthException error) when(error.Status is 403 or 404 or 410){ }
            http.Response.Cookies.Delete(RuntimeCookie,Cookie(DateTime.UtcNow));
            http.Response.Cookies.Delete(BindingCookie,Cookie(DateTime.UtcNow));return Results.NoContent();
        });
    }

    private static CookieOptions Cookie(DateTime expiry)=>new(){Path="/",Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Expires=new DateTimeOffset(DateTime.SpecifyKind(expiry,DateTimeKind.Utc)),IsEssential=true};
    internal static string? UniqueCookie(HttpContext http,string name)
    {
        if(http.Request.Headers.Cookie.ToString().Split(';').Count(part=>part.TrimStart().StartsWith(name+"=",StringComparison.Ordinal))!=1)return null;
        return http.Request.Cookies[name];
    }
}
