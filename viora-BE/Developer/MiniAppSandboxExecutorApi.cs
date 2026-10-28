using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class MiniAppSandboxExecutorApi
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/internal/sandbox-executor").RequireRateLimiting("mini-app-sandbox")
            .AddEndpointFilter<DeveloperApiFilter>().AddEndpointFilter<MiniAppConflictFilter>();
        group.AddEndpointFilter(async(context,next)=>{
            var http=context.HttpContext;var options=http.RequestServices.GetRequiredService<MiniAppSandboxOptions>();
            http.Response.Headers.CacheControl="no-store";http.Response.Headers["Referrer-Policy"]="no-referrer";
            if(!options.Enabled||http.Request.Scheme!="https"||http.Request.Host.Value!=new Uri(options.Issuer).Authority)return Results.NotFound();
            if(http.Request.Headers.Authorization.Count!=1||!http.RequestServices.GetRequiredService<MiniAppSandboxExecutorService>().Authenticate(http.Request.Headers.Authorization))
                throw new DeveloperAuthException(401,"SANDBOX_EXECUTOR_AUTH_REQUIRED","Cần thông tin xác thực executor riêng.");
            if(http.Request.QueryString.HasValue)throw MiniAppInput.Invalid("Executor không nhận tham số truy vấn.");
            return await next(context);
        });
        group.MapGet("/runs",async(MiniAppSandboxExecutorService service,CancellationToken ct)=>Results.Ok(await service.JobsAsync(ct)));
        group.MapPost("/evidence",async(HttpContext http,MiniAppSandboxExecutorService service,CancellationToken ct)=>{
            var request=await MiniAppSandboxApi.ReadRequest<SandboxExecutorReport>(http,ct,8192);
            await service.ReportAsync(request,Signature(http),ct);return Results.NoContent();
        });
        group.MapPost("/launch-grants",async(HttpContext http,MiniAppSandboxExecutorService service,MiniAppSandboxGrantService grants,CancellationToken ct)=>{
            var request=await MiniAppSandboxApi.ReadRequest<SandboxExecutorLaunch>(http,ct,8192);
            return Results.Ok(await service.LaunchAsync(request,Signature(http),grants,ct));
        });
    }
    private static string? Signature(HttpContext http)=>http.Request.Headers["X-Ankt-Sandbox-Signature"].Count==1?http.Request.Headers["X-Ankt-Sandbox-Signature"].ToString():null;
}
