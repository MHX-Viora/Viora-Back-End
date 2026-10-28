using Viora.Domain.Developers;

internal static class MiniAppSandboxRunFixture
{
    internal static async Task<MiniAppSandboxTestRun> Run(SandboxFixture f)
    {
        var session = await f.Service().CreateAsync(f.Actor, f.App.AppId, default);
        var run = MiniAppSandboxEvidencePersistenceTests.Run(session.Id, session.ConfigurationFingerprint);
        run.DeadlineAt = f.Clock.Now.UtcDateTime.AddMinutes(2);
        f.Db.Add(run); await f.Db.SaveChangesAsync();
        f.Clock.Now = new DateTimeOffset(run.CreatedAt, TimeSpan.Zero);
        run.StartedAt = run.CreatedAt; run.Status = SandboxTestRunStatus.Running;
        await f.Db.SaveChangesAsync(); return run;
    }
}
