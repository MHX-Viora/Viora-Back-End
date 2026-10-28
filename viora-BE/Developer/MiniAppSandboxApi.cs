using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppSandboxApi
{
    public static void AddServices(IServiceCollection services,IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MiniAppSandboxOptions>,MiniAppSandboxOptionsValidator>();
        services.AddOptions<MiniAppSandboxOptions>().Bind(configuration.GetSection(MiniAppSandboxOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton(provider=>provider.GetRequiredService<IOptions<MiniAppSandboxOptions>>().Value);
        services.AddScoped<MiniAppSandboxAccess>(); services.AddScoped<MiniAppSandboxSessionService>();
        services.AddScoped<MiniAppSandboxDiagnosticService>();
        services.AddScoped<MiniAppSandboxExecutorService>();
        services.AddScoped<MiniAppSandboxEvidenceReader>();
        services.AddScoped<MiniAppSandboxConfigurationService>();
        services.AddSingleton<MiniAppSandboxRunCancellation>();services.AddScoped<MiniAppSandboxRunCoordinator>();
        services.AddScoped<IMiniAppSandboxCheckExecutor,MiniAppSandboxCheckExecutor>();services.AddHostedService<MiniAppSandboxRunWorker>();
        services.AddScoped<MiniAppSandboxRetentionService>();services.AddHostedService<MiniAppSandboxRetentionWorker>();
        services.AddScoped<MiniAppSandboxNetwork>();
        services.AddDataProtection(); services.AddScoped<MiniAppSandboxGrantService>();
        services.AddRateLimiter(options=>options.AddPolicy("mini-app-sandbox",http=>RateLimitPartition.GetFixedWindowLimiter(
            (http.User.FindFirstValue("developer_id") is {} developer?"developer:"+developer:
                http.User.FindFirstValue("sub") is {} admin?"admin:"+admin:"ip:"+http.Connection.RemoteIpAddress),
            _=>new FixedWindowRateLimiterOptions{PermitLimit=60,Window=TimeSpan.FromMinutes(1),QueueLimit=0})));
        services.AddRateLimiter(options=>options.AddPolicy("mini-app-sandbox-runtime",http=>RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString()??"anonymous",_=>new FixedWindowRateLimiterOptions{PermitLimit=120,Window=TimeSpan.FromMinutes(1),QueueLimit=0})));
    }

    public static void MapEndpoints(RouteGroupBuilder developerGroup)
    {
        MapSessionRoutes(developerGroup.MapGroup("/{appId}/sandbox").RequireRateLimiting("mini-app-sandbox"),false);
    }

    public static void MapAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/mini-apps/{appId}/sandbox").RequireAuthorization(policy=>policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().RequireRole("2"))
            .RequireRateLimiting("mini-app-sandbox").AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>();
        MapSessionRoutes(group,true);
    }

    private static void MapSessionRoutes(RouteGroupBuilder group,bool admin)
    {
        group.MapGet("/readiness",async(string appId,HttpContext http,MiniAppSandboxConfigurationService service,CancellationToken ct)=>
        {
            if(http.Request.Query.Count!=0)throw MiniAppInput.Invalid("Readiness không nhận tham số truy vấn.");
            return Results.Ok(await service.ReadinessAsync(Actor(http,admin),appId,ct));
        });
        group.MapGet("/config",async(string appId,HttpContext http,MiniAppSandboxAccess access,MiniAppSandboxOptions options,CancellationToken ct)=>
        {
            await access.RequireAsync(Actor(http,admin),appId,ct);
            return Results.Ok(new{hostOrigin=new Uri(options.HostOrigin).GetLeftPart(UriPartial.Authority)});
        });
        group.AddEndpointFilter(async(context,next)=>
        {
            var http=context.HttpContext; http.Response.Headers.CacheControl="no-store"; http.Response.Headers["Referrer-Policy"]="no-referrer";
            var result=await next(context);
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("MiniAppSandboxAudit")
                .LogInformation("Sandbox management {Method} {AppId} actor {ActorId} session {SessionId}",http.Request.Method,
                    http.Request.RouteValues["appId"],http.User.FindFirstValue(admin?"sub":"developer_id"),http.Request.RouteValues["sessionId"]);
            return result;
        });
        group.MapPost("/sessions",async(string appId,HttpContext http,MiniAppSandboxSessionService service,CancellationToken ct)=>
        {
            await RequireEmptyRequest(http,ct);
            var session=await service.CreateAsync(Actor(http,admin),appId,ct);
            return Results.Created((admin?"/api/admin/mini-apps/":"/api/developer/mini-apps/")+appId+"/sandbox/sessions/"+session.Id,session);
        });
        group.MapGet("/sessions/{sessionId:guid}",async(string appId,Guid sessionId,HttpContext http,MiniAppSandboxSessionService service,CancellationToken ct)=>
            Results.Ok(await service.GetAsync(Actor(http,admin),appId,sessionId,ct)));
        group.MapGet("/diagnostics",async(string appId,Guid sessionId,string? category,string? cursor,int? limit,HttpContext http,MiniAppSandboxDiagnosticService service,CancellationToken ct)=>
            Results.Ok(await service.ListAsync(Actor(http,admin),appId,sessionId,category,cursor,limit??50,ct)));
        group.MapPost("/test-runs",async(string appId,HttpContext http,MiniAppSandboxRunCoordinator service,CancellationToken ct)=>
        {
            var request=await ReadRequest<SandboxRunRequest>(http,ct,4096);var run=await service.CreateAsync(Actor(http,admin),appId,request,ct);
            return Results.Accepted((admin?"/api/admin/mini-apps/":"/api/developer/mini-apps/")+appId+"/sandbox/test-runs/"+run.Id,run);
        });
        group.MapGet("/test-runs",async(string appId,Guid sessionId,string? cursor,int? limit,string? order,HttpContext http,MiniAppSandboxRunCoordinator service,CancellationToken ct)=>{
            if(order is not (null or "newest"))throw MiniAppInput.Invalid("Thứ tự lượt kiểm tra không hợp lệ.");
            return Results.Ok(await service.ListAsync(Actor(http,admin),appId,sessionId,cursor,limit??20,ct,order=="newest"));
        });
        group.MapGet("/test-runs/{runId:guid}",async(string appId,Guid runId,HttpContext http,MiniAppSandboxRunCoordinator service,CancellationToken ct)=>
            Results.Ok(await service.GetAsync(Actor(http,admin),appId,runId,ct)));
        group.MapDelete("/test-runs/{runId:guid}",async(string appId,Guid runId,HttpContext http,MiniAppSandboxRunCoordinator service,CancellationToken ct)=>
        {await service.CancelAsync(Actor(http,admin),appId,runId,ct);return Results.NoContent();});
        group.MapDelete("/sessions/{sessionId:guid}",async(string appId,Guid sessionId,HttpContext http,MiniAppSandboxSessionService service,CancellationToken ct)=>
        {await service.RevokeAsync(Actor(http,admin),appId,sessionId,ct);return Results.NoContent();});
        group.MapPost("/sessions/{sessionId:guid}/launch-grants",async(string appId,Guid sessionId,HttpContext http,MiniAppSandboxGrantService service,CancellationToken ct)=>
        {
            var request=await ReadRequest<SandboxLaunchRequest>(http,ct);
            return Results.Ok(await service.IssueAsync(Actor(http,admin),appId,sessionId,request.BrowserNonce,ct));
        });
    }

    internal static SandboxActor Actor(HttpContext http,bool admin)
    {
        if(!Guid.TryParse(http.User.FindFirstValue(admin?"sub":"developer_id"),out var id) ||
            !Guid.TryParse(http.User.FindFirstValue(admin?"sid":"developer_session"),out var source) || id==Guid.Empty || source==Guid.Empty)
            throw new DeveloperAuthException(403,"SANDBOX_ACTOR_UNAVAILABLE","Phiên kiểm thử không hợp lệ.");
        return new(admin?SandboxActorKind.Admin:SandboxActorKind.Developer,id,source);
    }

    private static async Task RequireEmptyRequest(HttpContext http,CancellationToken ct)
    {
        var value=await ReadRequest<JsonElement>(http,ct);
        if(value.ValueKind!=JsonValueKind.Object || value.EnumerateObject().Any()) throw MiniAppInput.Invalid("Không truyền actor hoặc môi trường khi tạo phiên Sandbox.");
    }

    internal static async Task<T> ReadRequest<T>(HttpContext http,CancellationToken ct,int maximum=1024)
    {
        if(http.Request.ContentType?.Split(';')[0].Trim()!="application/json") throw MiniAppInput.Invalid("Cần nội dung JSON.");
        var buffer=new byte[maximum+1]; var length=0;
        while(length<buffer.Length)
        {
            var read=await http.Request.Body.ReadAsync(buffer.AsMemory(length),ct); if(read==0)break; length+=read;
        }
        if(length>maximum) throw new DeveloperAuthException(413,"SANDBOX_REQUEST_TOO_LARGE","Yêu cầu Sandbox vượt giới hạn.");
        try
        {
            using var document=JsonDocument.Parse(buffer.AsMemory(0,length),new JsonDocumentOptions {MaxDepth=4});
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw MiniAppInput.Invalid("Cần nội dung JSON object.");
            static void Unique(JsonElement element)
            {
                if(element.ValueKind==JsonValueKind.Object)
                {
                    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var property in element.EnumerateObject())
                    {if(!names.Add(property.Name))throw MiniAppInput.Invalid("Không lặp lại trường dữ liệu.");Unique(property.Value);}
                }
                else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())Unique(item);
            }
            Unique(document.RootElement);
            return document.RootElement.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=4})
                ?? throw MiniAppInput.Invalid("Nội dung JSON không hợp lệ.");
        }
        catch(JsonException){throw MiniAppInput.Invalid("Nội dung JSON không hợp lệ.");}
    }
}
