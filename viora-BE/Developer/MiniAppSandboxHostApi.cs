using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppSandboxHostApi
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/sandbox-host").RequireRateLimiting("mini-app-sandbox-runtime")
            .AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>();
        group.AddEndpointFilter(async(context,next)=>
        {
            var http=context.HttpContext; var options=http.RequestServices.GetRequiredService<MiniAppSandboxOptions>();
            http.Response.Headers.CacheControl="no-store"; http.Response.Headers["Referrer-Policy"]="no-referrer";
            http.Response.Headers["X-Content-Type-Options"]="nosniff";
            if(!options.Enabled || http.Request.Scheme!="https" || !string.Equals(http.Request.Host.Value,new Uri(options.HostOrigin).Authority,StringComparison.OrdinalIgnoreCase))return Results.NotFound();
            Policy(http,options,"'none'"); return await next(context);
        });
        group.MapGet("/config",(MiniAppSandboxOptions options)=>Results.Ok(new{portalOrigin=new Uri(options.PortalOrigin).GetLeftPart(UriPartial.Authority)}));
        group.MapGet("/host.js",()=>Results.Text(Embedded("Ankt.SandboxHost.Script"),"text/javascript; charset=utf-8"));
        group.MapGet("/bridge.mjs",()=>Results.Text(Embedded("Ankt.SandboxHost.Bridge"),"text/javascript; charset=utf-8"));
        group.MapGet("/host.css",()=>Results.Text(Style,"text/css; charset=utf-8"));
        // The anonymous shell cannot disclose registered app data or execute a partner.
        group.MapGet("/{sessionId:guid}",(Guid sessionId)=>Results.Content(Document,"text/html; charset=utf-8"));
        group.MapGet("/{sessionId:guid}/run",async(Guid sessionId,HttpContext http,MiniAppSandboxGrantService grants,MiniAppSandboxOptions options,MiniAppSandboxNetwork network,CancellationToken ct)=>
        {
            var session=await grants.RequireRuntimeAsync(MiniAppSandboxRuntimeApi.UniqueCookie(http,MiniAppSandboxRuntimeApi.RuntimeCookie),ct);
            if(session.Id!=sessionId)return Results.NotFound();
            await network.RequirePublicAsync(session.WebsiteUrl,ct);
            Policy(http,options,session.PartnerOrigin); return Results.Content(Document,"text/html; charset=utf-8");
        });
    }

    internal static string Embedded(string name)
    {
        using var stream=typeof(MiniAppSandboxHostApi).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing Sandbox host asset.");
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }

    private static void Policy(HttpContext http,MiniAppSandboxOptions options,string frameSource)
    {
        http.Response.Headers["Content-Security-Policy"]="default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' https: data:; base-uri 'none'; form-action 'self'; frame-src "+frameSource+"; frame-ancestors "+new Uri(options.PortalOrigin).GetLeftPart(UriPartial.Authority)+";";
        http.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=()";
    }

    private const string Document="""
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="icon" href="data:,"><link rel="stylesheet" href="/sandbox-host/host.css"><title>ANKT Mini App Sandbox</title></head>
        <body><header><button id="back" disabled aria-label="Quay lại Sandbox">←</button><strong id="app-name">ANKT Mini App</strong><span>Sandbox</span><button id="close" disabled>Đóng</button></header><main><h1 class="sr-only">Phiên Mini App Sandbox</h1><p id="status" role="status">Đang xác thực phiên Sandbox…</p><div id="preview"></div></main><script src="/sandbox-host/host.js" type="module"></script></body></html>
        """;
    private const string Style="""
        *{box-sizing:border-box}html,body{margin:0;height:100%;font:15px/1.5 system-ui,sans-serif;background:#f6f7f9;color:#172536}body{display:flex;flex-direction:column}header{display:flex;align-items:center;gap:12px;padding:8px max(12px,env(safe-area-inset-right)) 8px max(12px,env(safe-area-inset-left));border-bottom:1px solid #d6dde7;background:#fff}header strong{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}header span{color:#81560d;font-size:12px}button{min-height:36px;padding:6px 12px;border:1px solid #bdc9d8;border-radius:7px;background:white;color:#172536;cursor:pointer}button:focus-visible{outline:3px solid #125fb5;outline-offset:2px}button:disabled{opacity:.5;cursor:default}main{display:flex;flex:1;min-height:0;flex-direction:column}#status{font-size:12px;margin:0;padding:8px 12px;overflow-wrap:anywhere}#preview{flex:1;min-height:0}iframe{display:block;width:100%;height:100%;border:0;background:white}.sr-only{position:absolute;width:1px;height:1px;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap}
        """;
}
