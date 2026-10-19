using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxRetentionTests
{
    [Fact]
    public async Task ExpiryRevokesCredentialsConsumesGrantsAndEndsActiveRuns()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var run=MiniAppSandboxEvidencePersistenceTests.Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);f.Db.Add(MiniAppSandboxEvidencePersistenceTests.Result(run.Id));
        f.Db.MiniAppSandboxLaunchGrants.Add(new(){SandboxSessionId=session.Id,GrantHash=new string('a',64),BrowserNonceHash=new string('b',64),Audience=f.Options.HostOrigin,ExpiresAt=DateTime.UtcNow.AddSeconds(30)});
        await f.Db.SaveChangesAsync();var credential=new string('c',64);await f.Db.MiniAppSandboxSessions.Where(x=>x.Id==session.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RuntimeCredentialHash,credential));
        f.Clock.Now+=TimeSpan.FromMinutes(20);
        var result=await new MiniAppSandboxRetentionService(f.Db,f.Clock).SweepAsync(100,default);
        Assert.Equal(1,result.RevokedSessions);Assert.Equal(1,result.EndedRuns);
        var ended=await f.Db.MiniAppSandboxSessions.AsNoTracking().SingleAsync();Assert.NotNull(ended.RevokedAt);Assert.Null(ended.RuntimeCredentialHash);
        Assert.NotNull((await f.Db.MiniAppSandboxLaunchGrants.AsNoTracking().SingleAsync()).ConsumedAt);
        var stopped=await f.Db.MiniAppSandboxTestRuns.AsNoTracking().SingleAsync();Assert.Contains(stopped.Status,new[]{SandboxTestRunStatus.Failed,SandboxTestRunStatus.Cancelled});Assert.NotNull(stopped.CompletedAt);
        Assert.Equal(SandboxMessageCode.MissingEvidence,(await f.Db.MiniAppSandboxTestResults.AsNoTracking().SingleAsync()).MessageCode);
        var repeat=await new MiniAppSandboxRetentionService(f.Db,f.Clock).SweepAsync(100,default);Assert.Equal(0,repeat.RevokedSessions);Assert.Equal(0,repeat.EndedRuns);
    }

    [Fact]
    public async Task StopImmediatelyCancelsOnlyTheSessionsActiveRun()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        f.Db.Add(MiniAppSandboxEvidencePersistenceTests.Run(session.Id,session.ConfigurationFingerprint));await f.Db.SaveChangesAsync();
        await f.Service().RevokeAsync(f.Actor,f.App.AppId,session.Id,default);
        var run=await f.Db.MiniAppSandboxTestRuns.AsNoTracking().SingleAsync();Assert.Equal(SandboxTestRunStatus.Cancelled,run.Status);Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task RetentionPurgesDependentsInOrderAndPreservesActiveSessionsAndConsent()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var run=MiniAppSandboxEvidencePersistenceTests.Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        var check=MiniAppSandboxEvidencePersistenceTests.Result(run.Id);check.TestKey=SandboxCheckKey.Token;f.Db.Add(check);await f.Db.SaveChangesAsync();
        f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id,TestRunId=run.Id});
        f.Db.Add(new MiniAppSandboxProtocolEvidence {TestRunId=run.Id,TestKey=SandboxCheckKey.Token,Stage=SandboxProtocolStage.Token,EventKey=new string('a',64)});
        f.Db.MiniAppSandboxLaunchGrants.Add(new(){SandboxSessionId=session.Id,GrantHash=new string('b',64),BrowserNonceHash=new string('c',64),Audience=f.Options.HostOrigin,ExpiresAt=DateTime.UtcNow.AddMinutes(1)});
        var account=new Account {Email="retention-user@example.test"};f.Db.Add(account);f.Db.MiniAppUserConsents.Add(new(){AccountId=account.Id,MiniAppId=f.App.Id,OAuthClientId=f.Client.Id,GrantedScopes="openid",GrantedAt=DateTime.UtcNow});await f.Db.SaveChangesAsync();
        var old=f.Clock.Now.UtcDateTime.AddDays(-40);var expires=old.AddMinutes(15);var completed=old.AddMinutes(3);
        await f.Db.MiniAppSandboxSessions.Where(x=>x.Id==session.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old).SetProperty(x=>x.ExpiresAt,expires).SetProperty(x=>x.RevokedAt,completed));
        await f.Db.MiniAppSandboxTestRuns.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old).SetProperty(x=>x.DeadlineAt,expires).SetProperty(x=>x.CompletedAt,completed).SetProperty(x=>x.Status,SandboxTestRunStatus.Completed));
        await f.Db.MiniAppSandboxDiagnostics.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old));
        var grantExpiry=old.AddMinutes(1);await f.Db.MiniAppSandboxLaunchGrants.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old).SetProperty(x=>x.ExpiresAt,grantExpiry));
        var active=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=active.Id});await f.Db.SaveChangesAsync();
        // Even an artificially old diagnostic under an active session is preserved.
        await f.Db.MiniAppSandboxDiagnostics.Where(x=>x.SandboxSessionId==active.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old));
        var result=await new MiniAppSandboxRetentionService(f.Db,f.Clock).SweepAsync(100,default);
        Assert.Equal(1,result.PurgedRuns);Assert.Equal(1,result.PurgedSessions);Assert.Equal(1,result.PurgedGrants);Assert.Empty(f.Db.MiniAppSandboxProtocolEvidence);Assert.Empty(f.Db.MiniAppSandboxTestResults);
        Assert.Equal(active.Id,await f.Db.MiniAppSandboxSessions.Select(x=>x.Id).SingleAsync());Assert.Single(f.Db.MiniAppSandboxDiagnostics);Assert.Single(f.Db.MiniAppUserConsents);Assert.Single(f.Db.Accounts);
    }

    [Fact]
    public async Task RetentionRespectsTheSevenDayBoundaryAndBatchLimit()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        f.Db.AddRange(Enumerable.Range(0,3).Select(_=>new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id}));await f.Db.SaveChangesAsync();
        var old=f.Clock.Now.UtcDateTime.AddDays(-8);await f.Db.MiniAppSandboxDiagnostics.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,old));
        await f.Service().RevokeAsync(f.Actor,f.App.AppId,session.Id,default);
        var service=new MiniAppSandboxRetentionService(f.Db,f.Clock);var result=await service.SweepAsync(1,default);Assert.Equal(1,result.PurgedDiagnostics);Assert.Equal(2,await f.Db.MiniAppSandboxDiagnostics.CountAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>service.SweepAsync(101,default));
    }

    [Fact]
    public async Task SevenDayBoundaryPurgesOnlyEventsThatHaveReachedRetentionAge()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var exact=new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id};var newer=new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id};
        f.Db.AddRange(exact,newer);await f.Db.SaveChangesAsync();await f.Service().RevokeAsync(f.Actor,f.App.AppId,session.Id,default);
        var cutoff=f.Clock.Now.UtcDateTime.AddDays(-7);var after=cutoff.AddSeconds(1);
        await f.Db.MiniAppSandboxDiagnostics.Where(x=>x.Id==exact.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,cutoff));
        await f.Db.MiniAppSandboxDiagnostics.Where(x=>x.Id==newer.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,after));
        var result=await new MiniAppSandboxRetentionService(f.Db,f.Clock).SweepAsync(100,default);
        Assert.Equal(1,result.PurgedDiagnostics);Assert.Equal(newer.Id,await f.Db.MiniAppSandboxDiagnostics.Select(x=>x.Id).SingleAsync());
    }
}
