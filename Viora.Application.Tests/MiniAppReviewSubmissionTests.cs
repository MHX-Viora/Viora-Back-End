using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewSubmissionTests
{
    internal static MiniAppReviewSubmissionService Service(SandboxFixture f, AppDbContext? db = null)
    {
        db ??= f.Db; return new(db, new(db, f.Clock), MiniAppReviewReadinessTests.Service(f, db: db), f.Clock);
    }
    internal static async Task<MiniAppReviewSubmitRequest> Request(SandboxFixture f)
    {
        var draft = await MiniAppReviewReadinessTests.Ready(f);
        var appVersion = await f.Db.MiniApps.Select(x => x.Version).SingleAsync();
        return new(Guid.NewGuid(), draft.Id, draft.ConcurrencyToken, appVersion);
    }
    [Fact]
    public async Task SubmissionSealsExactSnapshotAndAttestationAndReplayNeedsNoNewRun()
    {
        await using var f = await SandboxFixture.Create(); var request = await Request(f); var service = Service(f);
        var result = await service.SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        Assert.Equal("PendingReview", result.Status); Assert.Equal(request.VersionId, result.VersionId);
        var version = await f.Db.MiniAppVersions.AsNoTracking().SingleAsync(); Assert.Equal(MiniAppVersionState.Submitted, version.State);
        var review = await f.Db.MiniAppReviewRequests.AsNoTracking().SingleAsync();
        var attestation = JsonSerializer.Deserialize<MiniAppReviewEvidenceAttestation>(review.EvidenceAttestationJson)!;
        Assert.Equal(version.SnapshotHash, attestation.SnapshotHash); Assert.Equal(version.Id, attestation.VersionId);
        Assert.Equal(18, attestation.Checks.Length); Assert.DoesNotContain("secret", review.EvidenceAttestationJson, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await f.Db.MiniAppReviewNotifications.ToListAsync()); Assert.Single(await f.Db.MiniAppReviewNotificationDeliveries.ToListAsync());
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
        f.Clock.Now += TimeSpan.FromMinutes(6);
        Assert.Equal(result, await service.SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default));
        Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request with { VersionId = Guid.NewGuid() }, default))).Status);
        await f.Db.DeveloperSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        Assert.Equal(403, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default))).Status);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("revision")]
    [InlineData("app-revision")]
    [InlineData("executor")]
    [InlineData("hash")]
    public async Task InvalidSubmissionNeverWritesReviewOrNotifications(string invalid)
    {
        await using var f = await SandboxFixture.Create(); var request = await Request(f);
        if (invalid == "executor") f.Options.ExecutorKey = "";
        if (invalid == "hash") await f.Db.MiniAppVersions.ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, new string('0', 64)));
        if (invalid == "revision") request = request with { ConcurrencyToken = Guid.NewGuid() };
        if (invalid == "app-revision") request = request with { AppVersion = Guid.NewGuid() };
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f).SubmitAsync(invalid == "foreign" ? Guid.NewGuid() : f.Actor.Id, f.Source.Id, f.App.AppId, request, default));
        Assert.Empty(await f.Db.MiniAppReviewRequests.ToListAsync()); Assert.Empty(await f.Db.MiniAppReviewNotifications.ToListAsync());
        Assert.Empty(await f.Db.MiniAppReviewNotificationDeliveries.ToListAsync()); Assert.Equal(MiniAppVersionState.Draft, await f.Db.MiniAppVersions.Select(x => x.State).SingleAsync());
    }

    private sealed class AuditFailure(SandboxClock clock, bool expire) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.ReviewSubmitted))
            { if (expire) clock.Now += TimeSpan.FromMinutes(6); else throw new InvalidOperationException("submission-audit-fixture"); }
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditFailureOrEvidenceExpiryAtCommitRollsBackTheEntireSubmission(bool expire)
    {
        await using var f = await SandboxFixture.Create(); var request = await Request(f);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new AuditFailure(f.Clock, expire)).Options);
        Func<Task> submit = async () => await Service(f, db).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        if (expire) await Assert.ThrowsAsync<DeveloperAuthException>(submit); else await Assert.ThrowsAsync<InvalidOperationException>(submit);
        Assert.Empty(await f.Db.MiniAppReviewRequests.AsNoTracking().ToListAsync()); Assert.Empty(await f.Db.MiniAppReviewNotifications.ToListAsync());
        Assert.Empty(await f.Db.MiniAppReviewNotificationDeliveries.ToListAsync()); Assert.Equal(MiniAppVersionState.Draft, await f.Db.MiniAppVersions.Select(x => x.State).SingleAsync());
        Assert.Empty(await f.Db.MiniAppReviewActions.Where(x => x.Action == MiniAppReviewActionKind.ReviewSubmitted).ToListAsync());
    }
}
