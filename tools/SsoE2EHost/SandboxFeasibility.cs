using System.Text.Json;

// Browser-policy experiment only. It makes no claim of authenticated Portal/runtime E2E.
internal static class SandboxFeasibility
{
    public static void Map(WebApplication app,string portal,string host)
    {
        app.MapGet("/fixture/sandbox",(HttpContext http)=>
        {
            Headers(http,"frame-src "+host+"; frame-ancestors 'none';");
            http.Response.Cookies.Append("__Host-FixturePortal","portal-fixture",Cookie());
            return Results.Content(Page("<h1>Stage 6 browser feasibility</h1><p>Controlled policy experiment; not a completed Sandbox.</p><iframe title='Isolated host' src='"+host+"/sandbox-host/feasibility' sandbox='allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox'></iframe><script src='/fixture/sandbox.js'></script>"),"text/html; charset=utf-8");
        });
        app.MapGet("/fixture/sandbox.js",(HttpContext http)=>
        {
            Headers(http,"");return Results.Text("document.querySelector('iframe').addEventListener('load',()=>{});","text/javascript");
        });
        app.MapGet("/sandbox-host/feasibility",(HttpContext http)=>
        {
            Headers(http,"frame-src https://localhost:5491 https://127.0.0.2:5492; frame-ancestors "+portal+";");
            http.Response.Cookies.Append("__Host-FixtureHost","host-fixture",Cookie());
            return Results.Content(Page("<h1>Isolated host policy test</h1><p id='cookie'></p><p id='status' role='status'>Awaiting real iframe signal</p><button id='public'>Public popup test</button><button id='confidential'>Confidential iframe</button><button id='blocked'>Blocked iframe</button><button id='restricted'>Block popups</button><div id='preview'></div><script src='/sandbox-host/feasibility.js'></script>"),"text/html; charset=utf-8");
        });
        app.MapGet("/sandbox-runtime/fixture-cookie",(HttpContext http)=>
        {
            Headers(http,"");return Results.Json(new{hostCookiePresent=http.Request.Cookies.ContainsKey("__Host-FixtureHost"),portalCookiePresent=http.Request.Cookies.ContainsKey("__Host-FixturePortal")});
        });
        app.MapGet("/sandbox-host/feasibility.js",(HttpContext http)=>
        {
            Headers(http,"");return Results.Text(Script,"text/javascript; charset=utf-8");
        });
    }
    private static CookieOptions Cookie()=>new(){Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromMinutes(15)};
    private static void Headers(HttpContext http,string extra)
    {
        http.Response.Headers.CacheControl="no-store";http.Response.Headers["Referrer-Policy"]="no-referrer";
        http.Response.Headers["Content-Security-Policy"]="default-src 'none'; script-src 'self'; connect-src 'self'; style-src 'unsafe-inline'; img-src data:; "+extra;
        http.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=(), payment=()";
    }
    private static string Page(string body)=>"<!doctype html><html lang=en><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><link rel=icon href='data:,'><title>Controlled Sandbox feasibility</title><style>body{font:16px system-ui;margin:20px}button{padding:10px;margin:4px}iframe{width:100%;height:680px;border:1px solid #ccc}p{overflow-wrap:anywhere}</style>"+body+"</html>";
    private const string Script="""
        const output=document.querySelector('#status'),preview=document.querySelector('#preview');let frame;
        fetch('/sandbox-runtime/fixture-cookie',{cache:'no-store'}).then(r=>r.json()).then(value=>{
          document.querySelector('#cookie').textContent='Host cookie: '+value.hostCookiePresent+'; Portal cookie sent to host: '+value.portalCookiePresent;
        });
        function open(kind){
          frame?.remove();frame=document.createElement('iframe');frame.title='Real controlled partner';
          frame.setAttribute('sandbox','allow-scripts allow-same-origin allow-forms'+(kind==='restricted'?'':' allow-popups allow-popups-to-escape-sandbox'));
          frame.referrerPolicy='no-referrer';frame.src=kind==='confidential'?'https://127.0.0.2:5492/':kind==='blocked'?'https://localhost:5491/fixture-blocked':'https://localhost:5491/fixture-feasibility';
          output.textContent='Awaiting real iframe signal; load alone is not Passed';preview.append(frame);
        }
        for(const button of document.querySelectorAll('button'))button.addEventListener('click',()=>open(button.id));
        window.addEventListener('message',event=>{
          const value=event.data;
          if(event.source!==frame?.contentWindow||event.origin!=='https://localhost:5491'||!value||typeof value!=='object'||Array.isArray(value)||
            Object.keys(value).sort().join(',')!=='channel,status,version'||value.channel!=='ankt.feasibility.runtime'||value.version!==1||
            !['initialized','awaiting_authorization','authenticated','userinfo_valid','popup_blocked','timeout','request_failed'].includes(value.status))return;
          output.textContent='Observed partner state: '+value.status;
        });
        """;
}
