using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxEndpointTests
{
    [Fact]
    public async Task ReadinessIsOwnerBoundBeforeLaunchAndAcceptsNoEnvironmentInput()
    {
        await using var host=await MiniAppApiTests.Host.Create(sandbox:true);var http=host.Client;
        http.DefaultRequestHeaders.Authorization=new("DeveloperSession",await host.Login("readiness-owner@api.test"));
        var app=(await (await http.PostAsJsonAsync("/api/developer/mini-apps",new MiniAppCreateRequest("Readiness","utilities",Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var path=$"/api/developer/mini-apps/{app.AppId}/sandbox/readiness";
        var response=await http.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("no-store",response.Headers.CacheControl!.ToString());
        var readiness=(await response.Content.ReadFromJsonAsync<SandboxReadinessDto>())!;Assert.False(readiness.ReadyForSandbox);Assert.False(readiness.ReadyForReview);Assert.Equal(18,readiness.Checks.Length);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.GetAsync(path+"?environment=Production")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await http.PostAsJsonAsync(path,new{status="Passed"})).StatusCode);
        http.DefaultRequestHeaders.Authorization=new("DeveloperSession",await host.Login("readiness-other@api.test"));
        Assert.Equal(HttpStatusCode.NotFound,(await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Authorization=null;Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task HttpAndWorkerScopesCanOwnIndependentTransactions()
    {
        await using var host=await MiniAppApiTests.Host.Create(sandbox:true);
        await host.WithServices(async first=>
        {
            var db=first.GetRequiredService<AppDbContext>();await db.Database.OpenConnectionAsync();
            await using var outer=((Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred:true);
            return await host.WithServices(async second=>
            {
                var other=second.GetRequiredService<AppDbContext>();await other.Database.OpenConnectionAsync();
                await using var inner=((Microsoft.Data.Sqlite.SqliteConnection)other.Database.GetDbConnection()).BeginTransaction(deferred:true);
                return true;
            });
        });
    }

    [Fact]
    public async Task DedicatedHostShellHasNarrowPolicyAndExposesNoPlatformAuthSurface()
    {
        await using var host=await MiniAppApiTests.Host.Create(sandbox:true); using var browser=host.RuntimeClient();
        browser.BaseAddress=new("https://sandbox.ankt.example"); var id=Guid.NewGuid();
        var shell=await browser.GetAsync("/sandbox-host/"+id); Assert.Equal(HttpStatusCode.OK,shell.StatusCode);
        var policy=shell.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors https://portal.ankt.example",policy); Assert.Contains("frame-src 'none'",policy);
        Assert.DoesNotContain("<iframe",await shell.Content.ReadAsStringAsync());
        Assert.False(shell.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.NotFound,(await browser.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await browser.GetAsync("/.well-known/openid-configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await host.Client.GetAsync("https://api.example.com/sandbox-host/"+id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await browser.GetAsync("/sandbox-host/"+id+"/run")).StatusCode);
    }

    [Fact]
    public async Task ActualDeveloperSessionOwnsSandboxAndCannotSupplyAnotherActorOrEnvironment()
    {
        await using var host=await MiniAppApiTests.Host.Create(sandbox:true); var http=host.Client;
        http.BaseAddress=new("https://api.example.com");
        var credential=await host.Login("sandbox-owner@api.test"); http.DefaultRequestHeaders.Authorization=new("DeveloperSession",credential);
        var appResponse=await http.PostAsJsonAsync("/api/developer/mini-apps",new MiniAppCreateRequest("Sandbox HTTP","utilities",Guid.NewGuid()));
        var app=(await appResponse.Content.ReadFromJsonAsync<MiniAppDto>())!;
        await host.WithServices(async services=>
        {
            var db=services.GetRequiredService<AppDbContext>(); var row=await db.MiniApps.SingleAsync(x=>x.AppId==app.AppId);
            row.WebsiteUrl="https://partner.example.com/app";
            (await db.MiniAppOAuthClients.SingleAsync()).SandboxEnabled=true;
            db.MiniAppDomainVerifications.Add(new(){MiniAppId=row.Id,Hostname="partner.example.com",VerificationFileName="sandbox-http.txt",Status=MiniAppDomainStatus.Verified,ExpiresAt=DateTime.UtcNow.AddHours(1)});
            await db.SaveChangesAsync(); return true;
        });
        var path=$"/api/developer/mini-apps/{app.AppId}/sandbox/sessions";
        var injected=await http.PostAsJsonAsync(path,new {developerAccountId=Guid.NewGuid(),environment="Production"});
        Assert.Equal(HttpStatusCode.BadRequest,injected.StatusCode);
        var created=await http.PostAsJsonAsync(path,new {}); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var session=(await created.Content.ReadFromJsonAsync<SandboxSessionDto>())!;
        Assert.Equal("Sandbox",session.Environment); Assert.Contains("no-store",created.Headers.CacheControl!.ToString());
        using var browser=host.RuntimeClient(); browser.BaseAddress=new("https://sandbox.ankt.example");
        browser.DefaultRequestHeaders.Add("Origin","https://sandbox.ankt.example");
        Assert.Equal(HttpStatusCode.NotFound,(await browser.GetAsync("/api/developer/profile")).StatusCode);
        using var wrongOrigin=new HttpRequestMessage(HttpMethod.Post,"https://sandbox.ankt.example/sandbox-runtime/prepare");
        wrongOrigin.Headers.Add("Origin","https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden,(await host.RuntimeClient().SendAsync(wrongOrigin)).StatusCode);
        var prepare=await browser.PostAsJsonAsync("/sandbox-runtime/prepare",new {}); Assert.Equal(HttpStatusCode.OK,prepare.StatusCode);
        var nonce=(await prepare.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("browserNonce").GetString()!;
        var bindingCookie=prepare.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        Assert.Contains("httponly",prepare.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant());
        var launch=await (await http.PostAsJsonAsync(path+"/"+session.Id+"/launch-grants",new SandboxLaunchRequest(nonce))).Content.ReadFromJsonAsync<SandboxLaunchDto>();
        browser.DefaultRequestHeaders.Add("Cookie",bindingCookie);
        var redeem=await browser.PostAsJsonAsync("/sandbox-runtime/redeem",new SandboxRedeemRequest(session.Id,launch!.Grant,nonce));
        Assert.Equal(HttpStatusCode.OK,redeem.StatusCode);
        var runtimeCookie=redeem.Headers.GetValues("Set-Cookie").Single(value=>value.StartsWith("__Host-AnktMiniAppSandbox=",StringComparison.Ordinal)).Split(';')[0];
        browser.DefaultRequestHeaders.Remove("Cookie"); browser.DefaultRequestHeaders.Add("Cookie",runtimeCookie);
        var context=await browser.GetAsync("/sandbox-runtime/context"); Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        Assert.DoesNotContain("credential",await context.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
        var observation=await browser.PostAsJsonAsync("/sandbox-runtime/sdk-observations",new SandboxDiagnosticBatch([new("Bridge","BRIDGE_READY",1)]));
        Assert.Equal(HttpStatusCode.OK,observation.StatusCode);
        var diagnosticPath=$"/api/developer/mini-apps/{app.AppId}/sandbox/diagnostics?sessionId={session.Id}";
        var diagnostics=await http.GetAsync(diagnosticPath);Assert.Equal(HttpStatusCode.OK,diagnostics.StatusCode);
        var events=(await diagnostics.Content.ReadFromJsonAsync<SandboxDiagnosticsPage>())!.Items;
        Assert.Equal("SdkReported",Assert.Single(events).EvidenceSource);
        using(var duplicate=new StringContent("{\"events\":[{\"category\":\"Bridge\",\"code\":\"BRIDGE_READY\",\"Code\":\"BRIDGE_ERROR\"}]}",System.Text.Encoding.UTF8,"application/json"))
            Assert.Equal(HttpStatusCode.BadRequest,(await browser.PostAsync("/sandbox-runtime/sdk-observations",duplicate)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await browser.PostAsJsonAsync("/sandbox-runtime/sdk-observations",new{events=new[]{new{category="Bridge",code="BRIDGE_READY",access_token="never-store"}}})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await browser.PostAsJsonAsync("/sandbox-runtime/sdk-observations",new{events=new[]{new{category="Bridge",code="BRIDGE_READY"}},source="TrustedBrowser"})).StatusCode);
        using(var oversized=new StringContent(new string('x',32769),System.Text.Encoding.UTF8,"application/json"))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await browser.PostAsync("/sandbox-runtime/sdk-observations",oversized)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await host.RuntimeClient().GetAsync("https://sandbox.ankt.example/sandbox-runtime/context")).StatusCode);
        var runsPath=$"/api/developer/mini-apps/{app.AppId}/sandbox/test-runs";
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsJsonAsync(runsPath,new{sessionId=session.Id,checks=new[]{"domain"},status="Passed"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsJsonAsync(runsPath,new{sessionId=session.Id,checks=new[]{"unknown"}})).StatusCode);
        var runResponse=await http.PostAsJsonAsync(runsPath,new SandboxRunRequest(session.Id,["domain","runtime"]));Assert.Equal(HttpStatusCode.Accepted,runResponse.StatusCode);
        var run=(await runResponse.Content.ReadFromJsonAsync<SandboxRunSummary>())!;Assert.Equal("Sandbox",run.Environment);
        Assert.Equal(HttpStatusCode.OK,(await http.GetAsync(runsPath+"?sessionId="+session.Id)).StatusCode);
        var other=await host.Login("sandbox-other@api.test"); http.DefaultRequestHeaders.Authorization=new("DeveloperSession",other);
        Assert.Equal(HttpStatusCode.NotFound,(await http.GetAsync(runsPath+"/"+run.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await http.DeleteAsync(runsPath+"/"+run.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await http.GetAsync(diagnosticPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await http.GetAsync(path+"/"+session.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await http.DeleteAsync(path+"/"+session.Id)).StatusCode);
        http.DefaultRequestHeaders.Authorization=new("DeveloperSession",credential);
        Assert.Equal(HttpStatusCode.NoContent,(await http.DeleteAsync(runsPath+"/"+run.Id)).StatusCode);
        var runDetail=(await (await http.GetAsync(runsPath+"/"+run.Id)).Content.ReadFromJsonAsync<SandboxRunDetail>())!;
        Assert.Contains(runDetail.Run.Status,new[]{"Cancelled","Completed"});Assert.NotEqual("Passed",Assert.Single(runDetail.Results,x=>x.Key=="runtime").Status);
        Assert.Equal(HttpStatusCode.NoContent,(await http.DeleteAsync(path+"/"+session.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await browser.GetAsync("/sandbox-runtime/context")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await browser.PostAsJsonAsync("/sandbox-runtime/sdk-observations",new SandboxDiagnosticBatch([new("Bridge","BRIDGE_READY")]))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await http.DeleteAsync(path+"/"+session.Id)).StatusCode);
        var current=await (await http.GetAsync(path+"/"+session.Id)).Content.ReadFromJsonAsync<SandboxSessionDto>(); Assert.NotNull(current!.RevokedAt);
        var adminId=Guid.NewGuid();var adminSource=Guid.NewGuid();
        await host.WithServices(async services=>
        {
            var db=services.GetRequiredService<AppDbContext>();
            db.Accounts.Add(new(){Id=adminId,Email="sandbox-admin@api.test",Role=Viora.Domain.Entities.AccountRole.Admin});
            db.RefreshTokens.Add(new(){AccountId=adminId,SessionId=adminSource,TokenHash="sandbox-admin-http",ExpiresAt=DateTime.UtcNow.AddHours(1)});
            await db.SaveChangesAsync();return true;
        });
        http.DefaultRequestHeaders.Authorization=new("Bearer",host.AdminToken(adminId,adminSource));
        var adminPath=$"/api/admin/mini-apps/{app.AppId}/sandbox/sessions";
        var readinessPath=$"/api/admin/mini-apps/{app.AppId}/sandbox/readiness";
        Assert.Equal(HttpStatusCode.Forbidden,(await http.GetAsync(readinessPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await http.PostAsJsonAsync(adminPath,new {})).StatusCode);
        await host.WithServices(services=>{services.GetRequiredService<MiniAppSandboxOptions>().AdminTesterIds=[adminId];return Task.FromResult(true);});
        Assert.Equal(HttpStatusCode.OK,(await http.GetAsync(readinessPath)).StatusCode);
        var adminCreated=await http.PostAsJsonAsync(adminPath,new{}); Assert.Equal(HttpStatusCode.Created,adminCreated.StatusCode);
        var adminSession=(await adminCreated.Content.ReadFromJsonAsync<SandboxSessionDto>())!;
        await host.WithServices(async services=>
        {
            var db=services.GetRequiredService<AppDbContext>();var row=await db.MiniAppSandboxSessions.SingleAsync(x=>x.Id==adminSession.Id);
            Assert.Equal(adminId,row.ActorAdminAccountId);Assert.Null(row.ActorDeveloperAccountId);Assert.NotEqual(adminId,row.DeveloperAccountId);return true;
        });
        Assert.Equal(HttpStatusCode.NoContent,(await http.DeleteAsync(adminPath+"/"+adminSession.Id)).StatusCode);
        http.DefaultRequestHeaders.Authorization=null;
        Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync(path+"/"+session.Id)).StatusCode);
    }
}
