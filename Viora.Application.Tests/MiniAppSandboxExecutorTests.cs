using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;
using Viora.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

public sealed class MiniAppSandboxExecutorTests
{
    internal static string Signature(string key,string challenge,string value)=>Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(key),Encoding.UTF8.GetBytes(challenge+"\n"+value)));
    private static MiniAppSandboxExecutorService Service(SandboxFixture f)=>new(f.Db,f.Service(),f.Options,f.Clock,new EphemeralDataProtectionProvider());
    [Fact]
    public async Task ExecutorRuntimeCannotRotateOrCloseOwnerRuntimeAndEndsWithItsRun()
    {
        await using var f=await SandboxFixture.Create();var run=await Run(f);var protection=new EphemeralDataProtectionProvider();
        var grants=new MiniAppSandboxGrantService(f.Db,f.Service(),f.Clock,f.Options,protection);
        var owner=grants.Prepare();var ownerGrant=await grants.IssueAsync(f.Actor,f.App.AppId,run.SandboxSessionId,owner.Nonce,default);
        var ownerCookie=await grants.RedeemAsync(run.SandboxSessionId,ownerGrant.Grant,owner.Nonce,owner.Binding,default);
        var executor=new MiniAppSandboxExecutorService(f.Db,f.Service(),f.Options,f.Clock,protection);
        var proof=Assert.Single(Assert.Single(await executor.JobsAsync(default)).Checks,x=>x.Key=="runtime");var prepared=grants.Prepare();
        var launch=await executor.LaunchAsync(new(proof.Challenge,prepared.Nonce),Signature(f.Options.ExecutorKey,proof.Challenge,prepared.Nonce),grants,default);
        var executorCookie=await grants.RedeemAsync(run.SandboxSessionId,launch.Grant,prepared.Nonce,prepared.Binding,default);
        Assert.Equal(run.SandboxSessionId,(await grants.RequireRuntimeAsync(ownerCookie.Credential,default)).Id);
        Assert.Equal(run.SandboxSessionId,(await grants.RequireRuntimeAsync(executorCookie.Credential,default)).Id);
        Assert.True(executorCookie.ExpiresAt<=run.DeadlineAt);
        Assert.Equal(run.Version,(await f.Db.MiniAppSandboxTestRuns.AsNoTracking().SingleAsync()).Version);
        await executor.ReportAsync(new(proof.Challenge,"Passed"),Signature(f.Options.ExecutorKey,proof.Challenge,"Passed"),default);
        var coordinator=new MiniAppSandboxRunCoordinator(f.Db,f.Service(),f.Clock,new NoExecution(),new());
        await grants.CloseRuntimeAsync(executorCookie.Credential,coordinator,default);
        Assert.Equal("Cancelled",(await coordinator.GetAsync(f.Actor,f.App.AppId,run.Id,default)).Run.Status);
        Assert.Null((await f.Db.MiniAppSandboxSessions.AsNoTracking().SingleAsync()).RevokedAt);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>grants.RequireRuntimeAsync(executorCookie.Credential,default));
        Assert.Equal(run.SandboxSessionId,(await grants.RequireRuntimeAsync(ownerCookie.Credential,default)).Id);
    }
    private sealed class NoExecution:IMiniAppSandboxCheckExecutor
    {
        public Task<SandboxCheckExecution> ExecuteAsync(Guid session,SandboxCheckKey key,CancellationToken ct)=>throw new InvalidOperationException();
    }
    [Theory]
    [InlineData("tampered")][InlineData("expired")][InlineData("version")][InlineData("configuration")][InlineData("source")]
    public async Task ExecutorRuntimeCookieFailsClosedAgainstCurrentServerScope(string change)
    {
        await using var f=await SandboxFixture.Create();var run=await Run(f);var protection=new EphemeralDataProtectionProvider();
        var grants=new MiniAppSandboxGrantService(f.Db,f.Service(),f.Clock,f.Options,protection);var executor=new MiniAppSandboxExecutorService(f.Db,f.Service(),f.Options,f.Clock,protection);
        var proof=Assert.Single(Assert.Single(await executor.JobsAsync(default)).Checks,x=>x.Key=="runtime");var prepared=grants.Prepare();
        var launch=await executor.LaunchAsync(new(proof.Challenge,prepared.Nonce),Signature(f.Options.ExecutorKey,proof.Challenge,prepared.Nonce),grants,default);
        var cookie=await grants.RedeemAsync(run.SandboxSessionId,launch.Grant,prepared.Nonce,prepared.Binding,default);var value=cookie.Credential;
        switch(change){case "tampered":value="e1."+new string('a',100);break;case "expired":f.Clock.Now+=TimeSpan.FromSeconds(121);break;case "version":run.Version=Guid.NewGuid();break;case "configuration":f.App.Version=Guid.NewGuid();break;case "source":f.Source.RevokedAt=DateTime.UtcNow;break;}
        await f.Db.SaveChangesAsync();await Assert.ThrowsAsync<DeveloperAuthException>(()=>grants.RequireRuntimeAsync(value,default));
    }
    [Fact]
    public async Task PostgreSqlExecutorBindingMigrationPreservesOwnerGrantsAndClosesExecutorGrantsOnRollback()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);var run=await Run(f);
        var protection=new EphemeralDataProtectionProvider();var grants=new MiniAppSandboxGrantService(f.Db,f.Service(),f.Clock,f.Options,protection);
        var owner=grants.Prepare();await grants.IssueAsync(f.Actor,f.App.AppId,run.SandboxSessionId,owner.Nonce,default);
        var executor=new MiniAppSandboxExecutorService(f.Db,f.Service(),f.Options,f.Clock,protection);var prepared=grants.Prepare();
        var proof=Assert.Single(Assert.Single(await executor.JobsAsync(default)).Checks,x=>x.Key=="runtime");
        await executor.LaunchAsync(new(proof.Challenge,prepared.Nonce),Signature(f.Options.ExecutorKey,proof.Challenge,prepared.Nonce),grants,default);
        var ownerId=await f.Db.MiniAppSandboxLaunchGrants.Where(x=>x.ExecutorRunId==null).Select(x=>x.Id).SingleAsync();
        var executorId=await f.Db.MiniAppSandboxLaunchGrants.Where(x=>x.ExecutorRunId!=null).Select(x=>x.Id).SingleAsync();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
        var migrator=f.Db.GetService<IMigrator>();var current=f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_AddSandboxExecutorLaunchBinding"));var previous=f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_AddMiniAppSandboxDiagnosticCounter"));
        var up=migrator.GenerateScript(previous,current);Assert.DoesNotContain("DROP TABLE",up);Assert.DoesNotContain("DELETE FROM",up);
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous));f.Db.ChangeTracker.Clear();
        Assert.Null(await f.Db.MiniAppSandboxLaunchGrants.Where(x=>x.Id==ownerId).Select(x=>x.ConsumedAt).SingleAsync());
        Assert.NotNull(await f.Db.MiniAppSandboxLaunchGrants.Where(x=>x.Id==executorId).Select(x=>x.ConsumedAt).SingleAsync());
        await f.Db.Database.ExecuteSqlRawAsync(up);
        Assert.Equal(2,await f.Db.MiniAppSandboxLaunchGrants.CountAsync());Assert.Equal(1,await f.Db.MiniAppSandboxSessions.CountAsync());Assert.Equal(1,await f.Db.MiniAppSandboxTestRuns.CountAsync());
        Assert.Null((await f.Db.MiniAppSandboxLaunchGrants.SingleAsync(x=>x.Id==ownerId)).ExecutorRunId);
        // Restore a bound old grant and prove bounded cleanup observes the restricted FK order.
        await f.Db.MiniAppSandboxLaunchGrants.Where(x=>x.Id==executorId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExecutorRunId,run.Id).SetProperty(x=>x.ExecutorRunVersion,run.Version));
        await f.Db.MiniAppSandboxTestRuns.Where(x=>x.Id==run.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxTestRunStatus.Completed).SetProperty(x=>x.CompletedAt,DateTime.UtcNow));
        f.Clock.Now+=TimeSpan.FromDays(31);await new MiniAppSandboxRetentionService(f.Db,f.Clock).SweepAsync(100,default);
        Assert.Empty(await f.Db.MiniAppSandboxTestRuns.ToArrayAsync());Assert.Empty(await f.Db.MiniAppSandboxLaunchGrants.ToArrayAsync());
        Assert.Equal(1,await f.Db.MiniApps.CountAsync());Assert.Equal(1,await f.Db.MiniAppOAuthClients.CountAsync());
    }
    internal static async Task<MiniAppSandboxTestRun> Run(SandboxFixture f)
    {
        var run=await MiniAppSandboxRunFixture.Run(f);f.Options.ExecutorKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        foreach(var key in new[]{SandboxCheckKey.Runtime,SandboxCheckKey.Bridge})f.Db.MiniAppSandboxTestResults.Add(new(){TestRunId=run.Id,TestKey=key});
        await f.Db.SaveChangesAsync();return run;
    }
    [Fact]
    public async Task SeparateAuthorityAndSignedBoundEvidenceCannotBeReplayed()
    {
        await using var f=await SandboxFixture.Create();var run=await Run(f);var service=Service(f);
        Assert.False(service.Authenticate("Bearer "+f.Options.ExecutorKey));Assert.False(service.Authenticate("DeveloperSession fake"));
        Assert.True(service.Authenticate("SandboxExecutor "+f.Options.ExecutorKey));
        var job=Assert.Single(await service.JobsAsync(default));var proof=Assert.Single(job.Checks,x=>x.Key=="runtime");
        var report=new SandboxExecutorReport(proof.Challenge,"Passed");
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.ReportAsync(report,new string('0',64),default));
        await service.ReportAsync(report,Signature(f.Options.ExecutorKey,proof.Challenge,"Passed"),default);
        var row=Assert.Single(await f.Db.MiniAppSandboxDiagnostics.ToArrayAsync());Assert.Equal(SandboxEvidenceSource.TrustedBrowser,row.EvidenceSource);Assert.Equal(run.Id,row.TestRunId);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.ReportAsync(report,Signature(f.Options.ExecutorKey,proof.Challenge,"Passed"),default));
        Assert.All(await f.Db.MiniAppSandboxTestResults.ToArrayAsync(),x=>Assert.NotEqual(SandboxCheckStatus.Passed,x.Status));
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("cancelled")]
    [InlineData("configuration")]
    [InlineData("signature-status")]
    public async Task StaleOrTamperedProofCannotProduceTrustedEvidence(string kind)
    {
        await using var f=await SandboxFixture.Create();var run=await Run(f);var service=Service(f);
        var proof=Assert.Single(Assert.Single(await service.JobsAsync(default)).Checks,x=>x.Key=="runtime");
        switch(kind){case "expired":f.Clock.Now+=TimeSpan.FromSeconds(61);break;case "cancelled":run.Status=SandboxTestRunStatus.Cancelled;run.CompletedAt=DateTime.UtcNow;break;case "configuration":f.App.Version=Guid.NewGuid();break;}
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.ReportAsync(new(proof.Challenge,kind=="signature-status"?"Failed":"Passed"),Signature(f.Options.ExecutorKey,proof.Challenge,"Passed"),default));
        Assert.Empty(await f.Db.MiniAppSandboxDiagnostics.ToArrayAsync());
    }
    [Fact]
    public async Task PostgreSqlConcurrentProofsForSameCheckHaveOneWinner()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);await Run(f);
        var protection=new EphemeralDataProtectionProvider();var service=new MiniAppSandboxExecutorService(f.Db,f.Service(),f.Options,f.Clock,protection);
        var first=Assert.Single(Assert.Single(await service.JobsAsync(default)).Checks,x=>x.Key=="runtime");
        var second=Assert.Single(Assert.Single(await service.JobsAsync(default)).Checks,x=>x.Key=="runtime");
        async Task<bool> Submit(string challenge){
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options);
            var sessions=new MiniAppSandboxSessionService(db,new MiniAppSandboxAccess(db,f.Clock,f.Options),f.Clock,f.Options);
            try{await new MiniAppSandboxExecutorService(db,sessions,f.Options,f.Clock,protection).ReportAsync(new(challenge,"Passed"),Signature(f.Options.ExecutorKey,challenge,"Passed"),default);return true;}
            catch(DeveloperAuthException){return false;}
        }
        Assert.Single(await Task.WhenAll(Submit(first.Challenge),Submit(second.Challenge)),x=>x);
        Assert.Equal(1,await f.Db.MiniAppSandboxDiagnostics.CountAsync());
    }
    [Fact]
    public async Task InternalHttpSurfaceNeverAcceptsDeveloperOrAdminAuthentication()
    {
        await using var host=await MiniAppApiTests.Host.Create(sandbox:true);var http=host.Client;
        http.BaseAddress=new("https://identity.ankt.example");
        var key=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await host.WithServices(s=>{s.GetRequiredService<MiniAppSandboxOptions>().ExecutorKey=key;return Task.FromResult(true);});
        const string path="/internal/sandbox-executor/runs";
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Authorization=new("DeveloperSession",await host.Login("executor-owner@api.test"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Authorization=new("Bearer",key);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await http.GetAsync(path)).StatusCode);
        http.DefaultRequestHeaders.Authorization=new("SandboxExecutor",key);
        Assert.Equal(System.Net.HttpStatusCode.OK,(await http.GetAsync(path)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,(await http.GetAsync(path+"?status=Passed")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,(await http.GetAsync("https://sandbox.ankt.example"+path)).StatusCode);
    }
}
