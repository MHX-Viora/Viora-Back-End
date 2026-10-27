using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxRunTests
{
    private static MiniAppSandboxRunCoordinator Coordinator(SandboxFixture f,IMiniAppSandboxCheckExecutor? checks=null,MiniAppSandboxRunCancellation? cancellation=null)=>
        new(f.Db,f.Service(),f.Clock,checks??new UnavailableCheck(),cancellation??new());

    private sealed class UnavailableCheck : IMiniAppSandboxCheckExecutor
    {
        public Task<SandboxCheckExecution> ExecuteAsync(Guid id,SandboxCheckKey key,CancellationToken ct)=>
            Task.FromResult(new SandboxCheckExecution(SandboxCheckStatus.NotImplemented,SandboxMessageCode.MissingEvidence,SandboxEvidenceSource.Server));
    }
    [Fact]
    public async Task NewestFirstHistoryFindsActiveRunBeyondOldestPageAndPaginatesWithoutLoss()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Coordinator(f);
        var expected=new List<Guid>();
        for(var i=0;i<4;i++){
            var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);expected.Add(run.Id);
            if(i<3)await service.CancelAsync(f.Actor,f.App.AppId,run.Id,default);
        }
        var first=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,2,default,true);
        Assert.Equal(expected[3],first.Items[0].Id);Assert.Equal("Queued",first.Items[0].Status);
        var second=await service.ListAsync(f.Actor,f.App.AppId,session.Id,first.NextCursor,2,default,true);
        Assert.Null(second.NextCursor);Assert.Equal(expected.AsEnumerable().Reverse(),first.Items.Concat(second.Items).Select(x=>x.Id));
        var oldest=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,2,default);Assert.Equal(expected[0],oldest.Items[0].Id);
    }

    [Fact]
    public async Task OwnerQueuesFixedChecksAndCancellationIsIdempotent()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Coordinator(f);
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain","runtime"]),default);
        Assert.Equal("Queued",run.Status);Assert.True(run.DeadlineAt<=session.ExpiresAt);Assert.True(run.DeadlineAt<=f.Clock.Now.UtcDateTime.AddSeconds(120));
        var detail=await service.GetAsync(f.Actor,f.App.AppId,run.Id,default);Assert.Equal(2,detail.Results.Length);Assert.All(detail.Results,x=>Assert.Equal("Skipped",x.Status));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.CreateAsync(f.Actor,f.App.AppId,new(session.Id),default));
        await service.CancelAsync(f.Actor,f.App.AppId,run.Id,default);await service.CancelAsync(f.Actor,f.App.AppId,run.Id,default);
        Assert.Equal("Cancelled",(await service.GetAsync(f.Actor,f.App.AppId,run.Id,default)).Run.Status);
        Assert.NotEqual(run.Id,(await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default)).Id);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("expired")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    public async Task InvalidRequestsCreateNoRun(string kind)
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var actor=f.Actor;string[] keys=["domain"];
        switch(kind){case "other":actor=actor with {Id=Guid.NewGuid()};break;case "expired":f.Clock.Now+=TimeSpan.FromMinutes(20);break;case "unknown":keys=["0"];break;case "duplicate":keys=["domain","domain"];break;case "empty":keys=[];break;}
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>Coordinator(f).CreateAsync(actor,f.App.AppId,new(session.Id,keys),default));Assert.Empty(f.Db.MiniAppSandboxTestRuns);
    }

    [Fact]
    public async Task ProcessingNeverManufacturesPassedAndRequiresFreshOwnershipForReads()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Coordinator(f);
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain","runtime"]),default);
        Assert.True(await service.ExecuteNextAsync(default));Assert.False(await service.ExecuteNextAsync(default));
        var detail=await service.GetAsync(f.Actor,f.App.AppId,run.Id,default);Assert.Equal("Completed",detail.Run.Status);Assert.All(detail.Results,x=>Assert.Equal("NotImplemented",x.Status));
        var other=f.Actor with {Id=Guid.NewGuid()};await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.GetAsync(other,f.App.AppId,run.Id,default));await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.CancelAsync(other,f.App.AppId,run.Id,default));
        var page=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,1,default);Assert.Single(page.Items);Assert.Null(page.NextCursor);
    }

    private sealed class BlockingCheck : IMiniAppSandboxCheckExecutor
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SandboxCheckExecution> ExecuteAsync(Guid id,SandboxCheckKey key,CancellationToken ct)
        {Started.SetResult();await Task.Delay(Timeout.Infinite,ct);throw new InvalidOperationException();}
    }

    [Fact]
    public async Task LocalCancellationInterruptsTheCheckAndCannotRestoreCompletedState()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var registry=new MiniAppSandboxRunCancellation();var check=new BlockingCheck();var service=Coordinator(f,check,registry);
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);
        var processing=service.ExecuteNextAsync(default);await check.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.CancelAsync(f.Actor,f.App.AppId,run.Id,default);await processing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Cancelled",(await service.GetAsync(f.Actor,f.App.AppId,run.Id,default)).Run.Status);
    }

    [Fact]
    public async Task RestartReconcilesAbandonedRunningWorkOnlyAtItsDeadline()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Coordinator(f);
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);var started=DateTime.UtcNow;
        await f.Db.MiniAppSandboxTestRuns.Where(x=>x.Id==run.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxTestRunStatus.Running).SetProperty(x=>x.StartedAt,started));
        Assert.False(await Coordinator(f).ExecuteNextAsync(default));Assert.Equal("Running",(await service.GetAsync(f.Actor,f.App.AppId,run.Id,default)).Run.Status);
        f.Clock.Now+=TimeSpan.FromSeconds(121);Assert.False(await Coordinator(f).ExecuteNextAsync(default));
        var detail=await service.GetAsync(f.Actor,f.App.AppId,run.Id,default);Assert.Equal("Failed",detail.Run.Status);Assert.Equal("MISSING_EVIDENCE",Assert.Single(detail.Results).MessageCode);
        Assert.NotEqual(run.Id,(await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default)).Id);
    }

    private sealed class ExpiringCheck(SandboxClock clock) : IMiniAppSandboxCheckExecutor
    {
        public Task<SandboxCheckExecution> ExecuteAsync(Guid id,SandboxCheckKey key,CancellationToken ct)
        {clock.Now+=TimeSpan.FromMinutes(3);return Task.FromResult(new SandboxCheckExecution(SandboxCheckStatus.Passed,SandboxMessageCode.CheckPassed,SandboxEvidenceSource.Server,clock.Now.UtcDateTime));}
    }
    [Fact]
    public async Task ResultArrivingAfterTheDeadlineCannotBeCommittedOrCompleteTheRun()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Coordinator(f,new ExpiringCheck(f.Clock));
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);await service.ExecuteNextAsync(default);
        var detail=await service.GetAsync(f.Actor,f.App.AppId,run.Id,default);Assert.Equal("Failed",detail.Run.Status);Assert.Equal("Skipped",Assert.Single(detail.Results).Status);
    }

    [Fact]
    public async Task GlobalQueueCapAndPaginationStayBounded()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        // Isolate the global queue bound from the separate per-actor session-creation quota.
        for(var i=0;i<64;i++)
        {
            var row=new MiniAppSandboxSession {MiniAppId=f.App.Id,DeveloperAccountId=f.App.DeveloperAccountId,OAuthClientId=f.Client.Id,ActorDeveloperAccountId=f.Actor.Id,SourceDeveloperSessionId=f.Source.Id,
                ConfigurationFingerprint=session.ConfigurationFingerprint,WebsiteUrl=f.App.WebsiteUrl!,PartnerOrigin="https://partner.example.com",ExpiresAt=session.ExpiresAt,LastActivityAt=DateTime.UtcNow};
            f.Db.Add(row);f.Db.Add(MiniAppSandboxEvidencePersistenceTests.Run(row.Id,session.ConfigurationFingerprint));
        }
        await f.Db.SaveChangesAsync();var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>Coordinator(f).CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default));
        Assert.Equal(429,error.Status);Assert.Equal(64,await f.Db.MiniAppSandboxTestRuns.CountAsync());
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>Coordinator(f).ListAsync(f.Actor,f.App.AppId,session.Id,null,101,default));
    }

    private sealed class CountingCheck : IMiniAppSandboxCheckExecutor
    {
        public int Calls;
        public Task<SandboxCheckExecution> ExecuteAsync(Guid id,SandboxCheckKey key,CancellationToken ct)
        {Calls++;return Task.FromResult(new SandboxCheckExecution(SandboxCheckStatus.NotImplemented,SandboxMessageCode.MissingEvidence,SandboxEvidenceSource.Server));}
    }
    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("suspended")]
    [InlineData("expired")]
    public async Task EligibilityChangesBeforeExecutionPreventTheCheckFromRunning(string change)
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var check=new CountingCheck();var service=Coordinator(f,check);
        var run=await service.CreateAsync(f.Actor,f.App.AppId,new(session.Id,["domain"]),default);
        switch(change){case "source":f.Source.RevokedAt=DateTime.UtcNow;break;case "configuration":f.App.Version=Guid.NewGuid();break;case "suspended":f.App.Status=MiniAppStatus.Suspended;break;case "expired":f.Clock.Now+=TimeSpan.FromMinutes(20);break;}
        await f.Db.SaveChangesAsync();await service.ExecuteNextAsync(default);Assert.Equal(0,check.Calls);
        var row=await f.Db.MiniAppSandboxTestRuns.AsNoTracking().SingleAsync(x=>x.Id==run.Id);Assert.Equal(SandboxTestRunStatus.Failed,row.Status);
        Assert.All(await f.Db.MiniAppSandboxTestResults.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(SandboxMessageCode.MissingEvidence,x.MessageCode));
    }
}
