using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppSandboxRunPostgresTests
{
    private sealed class CountingCheck : IMiniAppSandboxCheckExecutor
    {
        public int Calls;
        public Task<SandboxCheckExecution> ExecuteAsync(Guid sessionId,SandboxCheckKey key,CancellationToken ct)
        {Interlocked.Increment(ref Calls);return Task.FromResult(new SandboxCheckExecution(SandboxCheckStatus.NotImplemented,SandboxMessageCode.MissingEvidence,SandboxEvidenceSource.Server));}
    }
    [Fact]
    public async Task ConcurrentRequestsQueueOneRunAndWorkersExecuteItsChecksOnce()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready=0;var go=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var registry=new MiniAppSandboxRunCancellation();var checks=new CountingCheck();
        MiniAppSandboxRunCoordinator Service(AppDbContext db)=>new(db,new MiniAppSandboxSessionService(db,new MiniAppSandboxAccess(db,f.Clock,f.Options),f.Clock,f.Options),f.Clock,checks,registry);
        async Task<SandboxRunSummary?> Create()
        {
            await using var db=new AppDbContext(options);if(Interlocked.Increment(ref ready)==2)go.SetResult();await go.Task;
            try{return await Service(db).CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);}
            catch(DeveloperAuthException error) when(error.Status==409){return null;}
        }
        var winner=Assert.Single(await Task.WhenAll(Create(),Create()),x=>x!=null)!;
        async Task<bool> Process(){await using var db=new AppDbContext(options);return await Service(db).ExecuteNextAsync(default);}
        Assert.Single(await Task.WhenAll(Process(),Process()),x=>x);Assert.Equal(1,checks.Calls);
        var detail=await Service(f.Db).GetAsync(f.Actor,f.App.AppId,winner.Id,default);Assert.Equal("Completed",detail.Run.Status);Assert.Equal("NotImplemented",Assert.Single(detail.Results).Status);
    }

    private sealed class LateCheck : IMiniAppSandboxCheckExecutor
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SandboxCheckExecution> Complete {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SandboxCheckExecution> ExecuteAsync(Guid sessionId,SandboxCheckKey key,CancellationToken ct){Started.SetResult();return Complete.Task;}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrSessionStopInterruptsLocalWorkAndLateResultCannotRestoreIt(bool stopSession)
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var registry=new MiniAppSandboxRunCancellation();var check=new LateCheck();
        var sessions=new MiniAppSandboxSessionService(f.Db,new MiniAppSandboxAccess(f.Db,f.Clock,f.Options),f.Clock,f.Options,registry);
        var service=new MiniAppSandboxRunCoordinator(f.Db,sessions,f.Clock,check,registry);var session=await sessions.CreateAsync(f.Actor,f.App.AppId,default);
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);
        await using var workerDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options);
        var worker=new MiniAppSandboxRunCoordinator(workerDb,new MiniAppSandboxSessionService(workerDb,new MiniAppSandboxAccess(workerDb,f.Clock,f.Options),f.Clock,f.Options,registry),f.Clock,check,registry);
        var processing=worker.ExecuteNextAsync(default);await check.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if(stopSession)await sessions.RevokeAsync(f.Actor,f.App.AppId,session.Id,default);else await service.CancelAsync(f.Actor,f.App.AppId,run.Id,default);
        await processing.WaitAsync(TimeSpan.FromSeconds(10));
        check.Complete.SetResult(new(SandboxCheckStatus.Passed,SandboxMessageCode.CheckPassed,SandboxEvidenceSource.Server,f.Clock.Now.UtcDateTime));
        var detail=await service.GetAsync(f.Actor,f.App.AppId,run.Id,default);Assert.Equal("Cancelled",detail.Run.Status);Assert.Equal("Skipped",Assert.Single(detail.Results).Status);Assert.Equal("MISSING_EVIDENCE",detail.Results[0].MessageCode);
        Assert.Empty(await f.Db.MiniAppSandboxProtocolEvidence.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConcurrentDifferentSessionsCannotOverfillTheGlobalQueue()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);Guid second=Guid.Empty;
        // Exercise global queue capacity independently from the session-creation actor quota.
        for(var i=0;i<64;i++)
        {
            var row=new MiniAppSandboxSession {MiniAppId=f.App.Id,DeveloperAccountId=f.App.DeveloperAccountId,OAuthClientId=f.Client.Id,ActorDeveloperAccountId=f.Actor.Id,SourceDeveloperSessionId=f.Source.Id,
                ConfigurationFingerprint=session.ConfigurationFingerprint,WebsiteUrl=f.App.WebsiteUrl!,PartnerOrigin="https://partner.example.com",ExpiresAt=session.ExpiresAt,LastActivityAt=DateTime.UtcNow};
            f.Db.Add(row);if(i<63)f.Db.Add(MiniAppSandboxEvidencePersistenceTests.Run(row.Id,session.ConfigurationFingerprint));else second=row.Id;
        }
        await f.Db.SaveChangesAsync();var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready=0;var go=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Create(Guid id)
        {
            await using var db=new AppDbContext(options);var service=new MiniAppSandboxRunCoordinator(db,new MiniAppSandboxSessionService(db,new MiniAppSandboxAccess(db,f.Clock,f.Options),f.Clock,f.Options),f.Clock,new CountingCheck(),new());
            if(Interlocked.Increment(ref ready)==2)go.SetResult();await go.Task;
            try{await service.CreateAsync(f.Actor,f.App.AppId,new(id,["domain"]),default);return true;}
            catch(DeveloperAuthException e) when(e.Status is 409 or 429){return false;}
        }
        Assert.Single(await Task.WhenAll(Create(session.Id),Create(second)),x=>x);Assert.Equal(64,await f.Db.MiniAppSandboxTestRuns.CountAsync());
    }
}
