using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewReadinessTests
{
    internal sealed class Fetcher : IPublicWebFetcher, IPublicJsonFetcher
    {
        public int Calls; public Func<Task>? DuringFetch;
        public async Task<PublicFetchResult> FetchAsync(Uri uri, int maxBytes, bool verificationFile, CancellationToken ct)
        { Calls++; if (DuringFetch != null) await DuringFetch(); return new("page"u8.ToArray(), "text/html"); }
        public Task<PublicFetchResult> FetchJsonAsync(Uri uri, int maxBytes, CancellationToken ct) => Task.FromResult(new PublicFetchResult("{}"u8.ToArray(), "application/json"));
    }
    internal static MiniAppReadinessService Service(SandboxFixture f, Fetcher? fetcher = null, AppDbContext? db = null)
    {
        db ??= f.Db; fetcher ??= new(); var sessions = new MiniAppSandboxSessionService(db, new(db, f.Clock, f.Options), f.Clock, f.Options);
        var reader = new MiniAppSandboxEvidenceReader(db, sessions, f.Options, f.Clock);
        return new(db, new(db, f.Clock), new(db, new(db, f.Clock), f.Clock),
            new(db, new(db, f.Clock, f.Options), fetcher, fetcher, f.Options, f.Clock, evidence: reader), reader, fetcher, f.Clock);
    }
    internal static async Task<MiniAppVersionSummaryDto> Ready(SandboxFixture f)
    {
        f.App.LogoUrl = "https://partner.example.com/logo.png"; f.App.ShortDescription = "Short"; f.App.Description = "Details";
        f.App.SupportEmail = "support@example.com"; f.App.PrivacyPolicyUrl = "https://partner.example.com/privacy";
        f.App.TermsOfServiceUrl = "https://partner.example.com/terms"; f.App.SsoEnabled = false; await f.Db.SaveChangesAsync();
        var draft = await MiniAppVersionTests.Service(f).CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), f.App.Version, "1.0"), default);
        await f.Db.Entry(f.App).ReloadAsync();
        var session = await f.Service().CreateAsync(f.Actor, f.App.AppId, default);
        f.Clock.Now = DateTimeOffset.UtcNow;
        var run = new MiniAppSandboxTestRun { SandboxSessionId = session.Id, ConfigurationFingerprint = session.ConfigurationFingerprint,
            Status = SandboxTestRunStatus.Queued, DeadlineAt = f.Clock.Now.UtcDateTime.AddMinutes(2) };
        f.Db.Add(run); foreach (var key in new[] { SandboxCheckKey.Runtime, SandboxCheckKey.Bridge }) f.Db.Add(new MiniAppSandboxTestResult { TestRunId = run.Id, TestKey = key });
        await f.Db.SaveChangesAsync(); f.Clock.Now = DateTimeOffset.UtcNow; run.Status = SandboxTestRunStatus.Running;
        run.StartedAt = f.Clock.Now.UtcDateTime; await f.Db.SaveChangesAsync();
        f.Options.ExecutorKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var executor = new MiniAppSandboxExecutorService(f.Db, f.Service(), f.Options, f.Clock, new EphemeralDataProtectionProvider());
        foreach (var check in Assert.Single(await executor.JobsAsync(default)).Checks)
            await executor.ReportAsync(new(check.Challenge, "Passed"), MiniAppSandboxExecutorTests.Signature(f.Options.ExecutorKey, check.Challenge, "Passed"), default);
        // PostgreSQL/parallel-suite latency must not make freshly persisted proof look future-dated.
        f.Clock.Now = DateTimeOffset.UtcNow;
        var evidence = new MiniAppSandboxEvidenceReader(f.Db, f.Service(), f.Options, f.Clock);
        foreach (var result in await f.Db.MiniAppSandboxTestResults.Where(x => x.TestRunId == run.Id).ToListAsync())
        {
            var observed = Assert.IsType<SandboxCheckExecution>(await evidence.ReadAsync(run.Id, result.TestKey, default));
            result.Status = observed.Status; result.MessageCode = observed.MessageCode; result.EvidenceSource = observed.EvidenceSource; result.EvidenceAt = observed.EvidenceAt;
        }
        f.Clock.Now = DateTimeOffset.UtcNow; run.Status = SandboxTestRunStatus.Completed; run.CompletedAt = f.Clock.Now.UtcDateTime; await f.Db.SaveChangesAsync(); return draft;
    }

    [Fact]
    public async Task ReviewNeedsFreshOwnedExecutorProofAndDoesNotRequireProductionWhenSsoIsOff()
    {
        await using var f = await SandboxFixture.Create(); var draft = await Ready(f); var service = Service(f);
        var ticket = await service.PrepareAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default);
        Assert.True(ticket.Readiness.ReadyForReview); Assert.Equal(draft.SnapshotHash, ticket.Readiness.SnapshotHash);
        Assert.NotNull(ticket.Readiness.SandboxRunId); Assert.Equal(18, ticket.Readiness.RequiredChecks.Length);
        await service.RevalidateAsync(ticket, f.Actor.Id, f.Source.Id, f.App.AppId, default);
        f.Clock.Now += TimeSpan.FromMinutes(6);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RevalidateAsync(ticket, f.Actor.Id, f.Source.Id, f.App.AppId, default));
    }

    [Theory]
    [InlineData("executor")]
    [InlineData("proof")]
    [InlineData("drift")]
    public async Task MissingOrStaleAuthorityNeverProducesReadyForReview(string invalid)
    {
        await using var f = await SandboxFixture.Create(); var draft = await Ready(f);
        if (invalid == "executor") f.Options.ExecutorKey = "";
        if (invalid == "proof") f.Proof.ExpiresAt = f.Clock.Now.UtcDateTime.AddSeconds(-1);
        if (invalid == "drift") f.App.Name = "Uncaptured change";
        await f.Db.SaveChangesAsync();
        var result = await Service(f).PrepareAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default);
        Assert.False(result.Readiness.ReadyForReview); Assert.NotEmpty(result.Readiness.BlockingReasons);
    }

    [Fact]
    public async Task ForeignOwnerDoesNoNetworkWorkAndPreflightRechecksChangesDuringIo()
    {
        await using var f = await SandboxFixture.Create(); var draft = await Ready(f); var fetcher = new Fetcher(); var service = Service(f, fetcher);
        Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PrepareAsync(Guid.NewGuid(), f.Source.Id, f.App.AppId, draft.Id, default))).Status);
        Assert.Equal(0, fetcher.Calls); var changed = false;
        fetcher.DuringFetch = async () => { if (changed) return; changed = true; f.App.Version = Guid.NewGuid(); await f.Db.SaveChangesAsync(); };
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PrepareAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default));
    }
    [Fact]
    public async Task AnotherLiveSessionOfTheSameOwnerCannotSubstituteForTheEvidenceSource()
    {
        await using var f = await SandboxFixture.Create(); var draft = await Ready(f);
        var other = new DeveloperSession { DeveloperAccountId = f.Actor.Id, TokenHash = "another-review-source", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1),
            AbsoluteExpiresAt = f.Clock.Now.UtcDateTime.AddHours(2) }; f.Db.Add(other); await f.Db.SaveChangesAsync();
        var ticket = await Service(f).PrepareAsync(f.Actor.Id, other.Id, f.App.AppId, draft.Id, default);
        Assert.False(ticket.Readiness.ReadyForReview); Assert.Contains("SANDBOX_FRESH_SOURCE_REQUIRED", ticket.Readiness.BlockingReasons);
    }
    [Fact]
    public async Task NetworkPreflightCannotRunInsideABusinessTransaction()
    {
        await using var f = await SandboxFixture.Create(); var draft = await Ready(f); var fetcher = new Fetcher(); var service = Service(f, fetcher);
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(f.Actor.Id, f.Source.Id, f.App.AppId, draft.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TargetAsync(f.Db.MiniAppVersions.Single(), true, default));
        Assert.Equal(0, fetcher.Calls);
    }
}
