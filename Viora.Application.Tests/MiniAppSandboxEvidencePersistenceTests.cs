using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Xunit;

public sealed class MiniAppSandboxEvidencePersistenceTests
{
    [Fact]
    public async Task ASessionHasOnlyOneActiveRunAndEachRunHasOneResultPerCheck()
    {
        await using var f=await SandboxFixture.Create();
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var first=Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(first);await f.Db.SaveChangesAsync();
        f.Db.Add(Run(session.Id,session.ConfigurationFingerprint));
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        var result=Result(first.Id);f.Db.Add(result);await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        f.Db.Add(Result(first.Id));await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        await f.Db.MiniAppSandboxTestRuns.Where(x=>x.Id==first.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,SandboxTestRunStatus.Cancelled).SetProperty(x=>x.CompletedAt,DateTime.UtcNow));
        f.Db.Add(Run(session.Id,session.ConfigurationFingerprint));await f.Db.SaveChangesAsync();
        Assert.Equal(2,await f.Db.MiniAppSandboxTestRuns.CountAsync());
    }

    [Theory]
    [InlineData(SandboxCheckKey.Runtime,SandboxEvidenceSource.Host)]
    [InlineData(SandboxCheckKey.Bridge,SandboxEvidenceSource.Sdk)]
    [InlineData(SandboxCheckKey.Runtime,SandboxEvidenceSource.Server)]
    [InlineData(SandboxCheckKey.Token,SandboxEvidenceSource.TrustedBrowser)]
    [InlineData(SandboxCheckKey.Domain,SandboxEvidenceSource.Sdk)]
    public async Task ReportedObservationsCannotBecomePassedChecks(SandboxCheckKey key,SandboxEvidenceSource source)
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var run=Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        var result=Result(run.Id);result.TestKey=key;result.Status=SandboxCheckStatus.Passed;result.EvidenceSource=source;result.EvidenceAt=DateTime.UtcNow;
        f.Db.Add(result);await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task DiagnosticsCannotBindAnotherSessionsRunAndDoNotStoreArbitraryText()
    {
        await using var f=await SandboxFixture.Create();var first=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var run=Run(first.Id,first.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        await f.Service().RevokeAsync(f.Actor,f.App.AppId,first.Id,default);
        var second=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=second.Id,TestRunId=run.Id,Code=SandboxMessageCode.BridgeReady,Category=SandboxDiagnosticCategory.Bridge,EvidenceSource=SandboxEvidenceSource.Host});
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=first.Id,TestRunId=run.Id,Code=SandboxMessageCode.BridgeReady,Category=SandboxDiagnosticCategory.Bridge,EvidenceSource=SandboxEvidenceSource.Host,Count=1});
        await f.Db.SaveChangesAsync();Assert.Single(await f.Db.MiniAppSandboxDiagnostics.ToListAsync());
        Assert.DoesNotContain(f.Db.Model.FindEntityType(typeof(MiniAppSandboxDiagnostic))!.GetProperties(),p=>p.ClrType==typeof(string));
        Assert.Empty(f.Db.MiniAppUserConsents);Assert.Empty(f.Db.AnktSsoSessions);
    }

    [Fact]
    public async Task AcceptedProofSourcesRemainDistinctAndMatchTheSessionSnapshot()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        f.Db.Add(Run(session.Id,new string('f',64)));await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        var run=Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        foreach(var (key,source) in new[]{(SandboxCheckKey.Domain,SandboxEvidenceSource.Server),(SandboxCheckKey.Runtime,SandboxEvidenceSource.TrustedBrowser),(SandboxCheckKey.Token,SandboxEvidenceSource.ServerProtocol)})
            f.Db.Add(new MiniAppSandboxTestResult {TestRunId=run.Id,TestKey=key,Status=SandboxCheckStatus.Passed,MessageCode=SandboxMessageCode.CheckPassed,EvidenceSource=source,EvidenceAt=DateTime.UtcNow});
        await f.Db.SaveChangesAsync();Assert.Equal(3,await f.Db.MiniAppSandboxTestResults.CountAsync());
        f.Db.ChangeTracker.Clear();
        var result=await f.Db.MiniAppSandboxTestResults.FirstAsync();var version=Guid.NewGuid();
        await f.Db.MiniAppSandboxTestResults.Where(x=>x.Id==result.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Version,version));
        result.Version=Guid.NewGuid();await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("run_environment")]
    [InlineData("run_status")]
    [InlineData("run_completion")]
    [InlineData("result_duration")]
    [InlineData("result_proof_time")]
    [InlineData("diagnostic_count")]
    [InlineData("diagnostic_code")]
    public async Task DatabaseRejectsInvalidCatalogsBoundsAndMissingProof(string kind)
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var run=Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        switch(kind)
        {
            case "run_environment":run.Environment=MiniAppOAuthEnvironment.Production;break;
            case "run_status":run.Status=(SandboxTestRunStatus)99;break;
            case "run_completion":run.Status=SandboxTestRunStatus.Completed;break;
            case "result_duration":var duration=Result(run.Id);duration.DurationMs=1800001;f.Db.Add(duration);break;
            case "result_proof_time":var proof=Result(run.Id);proof.Status=SandboxCheckStatus.Passed;f.Db.Add(proof);break;
            case "diagnostic_count":f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id,Count=1000001});break;
            case "diagnostic_code":f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id,Code=(SandboxMessageCode)999});break;
            default:throw new InvalidOperationException();
        }
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ProtocolEvidenceRequiresMatchingRunCheckAndOneUseEventKey()
    {
        await using var f=await SandboxFixture.Create();var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var run=Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        var evidence=new MiniAppSandboxProtocolEvidence {TestRunId=run.Id,TestKey=SandboxCheckKey.Token,Stage=SandboxProtocolStage.Token,Outcome=SandboxProtocolOutcome.TokenIssued,EventKey=new string('a',64),TokenId="internal-id-only"};
        f.Db.Add(evidence);await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        var result=Result(run.Id);result.TestKey=SandboxCheckKey.Token;f.Db.Add(result);await f.Db.SaveChangesAsync();
        evidence.Stage=SandboxProtocolStage.UserInfo;f.Db.Add(evidence);await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        evidence.Stage=SandboxProtocolStage.Token;f.Db.Add(evidence);await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        f.Db.Add(new MiniAppSandboxProtocolEvidence {TestRunId=run.Id,TestKey=SandboxCheckKey.Token,Stage=SandboxProtocolStage.Token,Outcome=SandboxProtocolOutcome.TokenIssued,EventKey=evidence.EventKey});
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());
    }

    internal static MiniAppSandboxTestRun Run(Guid session,string fingerprint)=>new(){SandboxSessionId=session,ConfigurationFingerprint=fingerprint,DeadlineAt=DateTime.UtcNow.AddMinutes(2)};
    internal static MiniAppSandboxTestResult Result(Guid run)=>new(){TestRunId=run,TestKey=SandboxCheckKey.Domain,Status=SandboxCheckStatus.Skipped,MessageCode=SandboxMessageCode.CheckPending};
}
