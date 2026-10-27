using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxReadinessEvidenceTests
{
    [Fact]
    public async Task BrowserReportsCannotPassWhileSignedExecutorProofIsFreshAndRevocable()
    {
        await using var f=await SandboxFixture.Create();var run=await MiniAppSandboxExecutorTests.Run(f);
        var reader=new MiniAppSandboxEvidenceReader(f.Db,f.Service(),f.Options,f.Clock);
        f.Db.MiniAppSandboxDiagnostics.Add(new(){SandboxSessionId=run.SandboxSessionId,TestRunId=run.Id,Category=SandboxDiagnosticCategory.Runtime,Code=SandboxMessageCode.RuntimeReady,EvidenceSource=SandboxEvidenceSource.Host});await f.Db.SaveChangesAsync();
        Assert.Null(await reader.ReadAsync(run.Id,SandboxCheckKey.Runtime,default));
        var executor=new MiniAppSandboxExecutorService(f.Db,f.Service(),f.Options,f.Clock,new EphemeralDataProtectionProvider());
        var proof=Assert.Single(Assert.Single(await executor.JobsAsync(default)).Checks,x=>x.Key=="runtime");
        await executor.ReportAsync(new(proof.Challenge,"Passed"),MiniAppSandboxExecutorTests.Signature(f.Options.ExecutorKey,proof.Challenge,"Passed"),default);
        var observed=(await reader.ReadAsync(run.Id,SandboxCheckKey.Runtime,default))!;Assert.Equal(SandboxCheckStatus.Passed,observed.Status);Assert.Equal(SandboxEvidenceSource.TrustedBrowser,observed.EvidenceSource);
        Assert.NotEqual(SandboxCheckStatus.Passed,(await reader.LatestAsync(f.App.Id,run.ConfigurationFingerprint,SandboxCheckKey.Runtime,default)).Status);
        var result=await f.Db.MiniAppSandboxTestResults.SingleAsync(x=>x.TestRunId==run.Id&&x.TestKey==SandboxCheckKey.Runtime);result.Status=observed.Status;result.EvidenceSource=observed.EvidenceSource;result.EvidenceAt=observed.EvidenceAt;result.MessageCode=observed.MessageCode;
        run.Status=SandboxTestRunStatus.Completed;run.CompletedAt=DateTime.UtcNow;await f.Db.SaveChangesAsync();
        Assert.Equal(SandboxCheckStatus.Passed,(await reader.LatestAsync(f.App.Id,run.ConfigurationFingerprint,SandboxCheckKey.Runtime,default)).Status);
        Assert.True(await reader.IsFreshAsync(run.Id,run.ConfigurationFingerprint,default));
        f.Source.RevokedAt=f.Clock.Now.UtcDateTime;await f.Db.SaveChangesAsync();
        Assert.False(await reader.IsFreshAsync(run.Id,run.ConfigurationFingerprint,default));
        Assert.NotEqual(SandboxCheckStatus.Passed,(await reader.LatestAsync(f.App.Id,run.ConfigurationFingerprint,SandboxCheckKey.Runtime,default)).Status);
        f.Source.RevokedAt=null;await f.Db.SaveChangesAsync();
        f.Clock.Now+=TimeSpan.FromMinutes(6);Assert.NotEqual(SandboxCheckStatus.Passed,(await reader.LatestAsync(f.App.Id,run.ConfigurationFingerprint,SandboxCheckKey.Runtime,default)).Status);
    }
    [Fact]
    public async Task DisabledExecutorNeverInventsRuntimeSuccess()
    {
        await using var f=await SandboxFixture.Create();var run=await MiniAppSandboxRunFixture.Run(f);
        var reader=new MiniAppSandboxEvidenceReader(f.Db,f.Service(),f.Options,f.Clock);
        var result=await reader.LatestAsync(f.App.Id,run.ConfigurationFingerprint,SandboxCheckKey.Runtime,default);
        Assert.Equal(SandboxCheckStatus.NotImplemented,result.Status);Assert.Equal(SandboxMessageCode.ExecutorUnavailable,result.MessageCode);
    }
}
