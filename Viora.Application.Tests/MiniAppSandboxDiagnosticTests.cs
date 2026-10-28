using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxDiagnosticTests
{
    private static SandboxDiagnosticBatch Batch(int count)=>new(Enumerable.Range(0,count).Select(_=>new SandboxDiagnosticEvent("Bridge","BRIDGE_READY",1)).ToArray());
    private static MiniAppSandboxDiagnosticService Service(SandboxFixture f)=>new(f.Db,f.Service(),f.Clock);

    [Fact]
    public async Task ObservationsAreCatalogOnlyAndReadsAreOwnedBoundedAndPaginated()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Service(f);
        Assert.Equal(3,(await service.IngestAsync(session.Id,SandboxEvidenceSource.Sdk,Batch(3),default)).AcceptedCount);
        var page=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,null,2,default);
        Assert.Equal(2,page.Items.Length);Assert.NotNull(page.NextCursor);Assert.All(page.Items,item=>{Assert.Equal("SdkReported",item.EvidenceSource);Assert.Equal("BRIDGE_READY",item.Code);});
        var second=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,page.NextCursor,2,default);
        Assert.Single(second.Items);Assert.Null(second.NextCursor);Assert.DoesNotContain(second.Items,x=>page.Items.Any(y=>x.Id==y.Id));
        var other=f.Actor with {Id=Guid.NewGuid()};await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.ListAsync(other,f.App.AppId,session.Id,null,null,2,default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.IngestAsync(session.Id,SandboxEvidenceSource.TrustedBrowser,Batch(1),default));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.ListAsync(f.Actor,f.App.AppId,session.Id,null,null,101,default));
        Assert.Empty(f.Db.MiniAppSandboxTestResults);
    }

    [Fact]
    public async Task MinuteQuotaRejectsWholeBatchAndPersistsBoundedDroppedCount()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var service=Service(f);
        for(var i=0;i<3;i++)await service.IngestAsync(session.Id,SandboxEvidenceSource.Host,Batch(20),default);
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.IngestAsync(session.Id,SandboxEvidenceSource.Host,Batch(5),default));
        Assert.Equal(429,error.Status);Assert.Equal(60,await f.Db.MiniAppSandboxDiagnostics.CountAsync());
        Assert.Equal(5,(await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,null,100,default)).DroppedCount);
        await f.Db.MiniAppSandboxSessions.Where(x=>x.Id==session.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DroppedDiagnosticCount,999999));
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>service.IngestAsync(session.Id,SandboxEvidenceSource.Host,Batch(5),default));
        Assert.Equal(1000000,(await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,null,100,default)).DroppedCount);
        await service.IngestAsync(session.Id,SandboxEvidenceSource.Host,Batch(4),default);Assert.Equal(64,await f.Db.MiniAppSandboxDiagnostics.CountAsync());
    }

    [Fact]
    public async Task OverallQuotaStillAppliesWhenTheMinuteWindowHasReset()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        f.Db.AddRange(Enumerable.Range(0,500).Select(_=>new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id,Category=SandboxDiagnosticCategory.Bridge,Code=SandboxMessageCode.BridgeReady,EvidenceSource=SandboxEvidenceSource.Sdk}));await f.Db.SaveChangesAsync();
        var old=f.Clock.Now.UtcDateTime.AddMinutes(-2);await f.Db.MiniAppSandboxDiagnostics.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old));
        var error=await Assert.ThrowsAsync<DeveloperAuthException>(()=>Service(f).IngestAsync(session.Id,SandboxEvidenceSource.Sdk,Batch(1),default));
        Assert.Equal(429,error.Status);Assert.Equal(500,await f.Db.MiniAppSandboxDiagnostics.CountAsync());
    }

    [Fact]
    public async Task PaginationUsesConsistentUuidCaseForIdenticalTimestamps()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var ids=new[]{"0000000a-0000-4000-8000-000000000000","0000000b-0000-4000-8000-000000000000","0000000c-0000-4000-8000-000000000000"};
        f.Db.MiniAppSandboxDiagnostics.AddRange(ids.Select(id=>new MiniAppSandboxDiagnostic {Id=Guid.Parse(id),SandboxSessionId=session.Id,Code=SandboxMessageCode.BridgeReady,Category=SandboxDiagnosticCategory.Bridge,EvidenceSource=SandboxEvidenceSource.Sdk}));await f.Db.SaveChangesAsync();
        var first=await Service(f).ListAsync(f.Actor,f.App.AppId,session.Id,null,null,2,default);
        var second=await Service(f).ListAsync(f.Actor,f.App.AppId,session.Id,null,first.NextCursor,2,default);
        Assert.Equal(ids.Select(Guid.Parse),first.Items.Concat(second.Items).Select(x=>x.Id));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("category")]
    [InlineData("count")]
    [InlineData("batch")]
    [InlineData("revoked")]
    [InlineData("run")]
    public async Task InvalidOrStaleBatchNeverPersistsAPartialObservation(string kind)
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var batch=Batch(2);
        switch(kind)
        {
            case "unknown":batch.Events[1]=new("Bridge","access_token=never-store");break;
            case "category":batch.Events[1]=new("SSO","BRIDGE_READY");break;
            case "count":batch.Events[1]=new("Bridge","BRIDGE_READY",-1);break;
            case "batch":batch=Batch(21);break;
            case "revoked":await f.Service().RevokeAsync(f.Actor,f.App.AppId,session.Id,default);break;
            case "run":batch=batch with {TestRunId=Guid.NewGuid()};break;
        }
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>Service(f).IngestAsync(session.Id,SandboxEvidenceSource.Sdk,batch,default));
        Assert.Empty(await f.Db.MiniAppSandboxDiagnostics.ToListAsync());
    }
}
