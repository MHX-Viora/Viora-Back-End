using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewApprovalTests
{
    [Fact]
    public async Task ApprovalTargetsTheSealedVersionAfterDraftChangesAndOriginalSourceExpiryWithoutPublishing()
    {
        await using var f = await SandboxFixture.Create(); var current = await MiniAppReviewDecisionTests.Started(f);
        var rootRevision = Guid.NewGuid();
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Unreviewed next draft").SetProperty(x => x.Version, rootRevision));
        var next = await MiniAppVersionTests.Service(f).CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), rootRevision, "2.0"), default);
        f.Clock.Now += TimeSpan.FromMinutes(6); await f.Db.DeveloperSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        var service = new MiniAppReviewDecisionService(f.Db, new(f.Db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f));
        var command = new MiniAppReviewDecisionRequest(Guid.NewGuid(), current.Review.ConcurrencyToken, null);
        var approved = await service.ApproveAsync(current.Admin, current.Session, current.Review.Id, command, default);
        Assert.Equal("Approved", approved.Status); Assert.Equal(approved, await service.ApproveAsync(current.Admin, current.Session, current.Review.Id, command, default));
        var detail = await new MiniAppReviewService(f.Db, new(f.Db, f.Clock)).GetAdminAsync(current.Admin, current.Session, approved.Id, default);
        Assert.Equal("Fixture", detail.Review.Snapshot.Name); Assert.NotEqual(next.Id, approved.VersionId);
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync()); Assert.Equal(3, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
    [Theory]
    [InlineData("domain")]
    [InlineData("developer")]
    [InlineData("evidence")]
    [InlineData("hash")]
    public async Task UnsafeSealedTargetsCannotBeApproved(string change)
    {
        await using var f = await SandboxFixture.Create(); var current = await MiniAppReviewDecisionTests.Started(f);
        if (change == "domain") await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(-1)));
        if (change == "developer") await f.Db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DeveloperStatus.Suspended));
        if (change == "evidence") await f.Db.MiniAppReviewRequests.ExecuteUpdateAsync(s => s.SetProperty(x => x.EvidenceAttestationJson, "{}"));
        if (change == "hash") await f.Db.MiniAppVersions.ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, new string('0', 64)));
        var service = new MiniAppReviewDecisionService(f.Db, new(f.Db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ApproveAsync(current.Admin, current.Session, current.Review.Id,
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, null), default));
        Assert.Equal(MiniAppReviewStatus.InReview, await f.Db.MiniAppReviewRequests.Select(x => x.Status).SingleAsync()); Assert.Equal(2, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
    [Fact]
    public async Task TargetChangesDuringNetworkPreflightAreRecheckedBeforeDecision()
    {
        await using var f = await SandboxFixture.Create(); var current = await MiniAppReviewDecisionTests.Started(f); var changed = false;
        var fetcher = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = async () =>
        { if (changed) return; changed = true; await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime).SetProperty(x => x.Status, MiniAppDomainStatus.Revoked)); } };
        var service = new MiniAppReviewDecisionService(f.Db, new(f.Db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f, fetcher));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ApproveAsync(current.Admin, current.Session, current.Review.Id,
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, null), default));
        Assert.Equal(MiniAppReviewStatus.InReview, await f.Db.MiniAppReviewRequests.Select(x => x.Status).SingleAsync());
    }
    private sealed class ExpireProof(SandboxClock clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.ReviewApproved)) clock.Now += TimeSpan.FromSeconds(2);
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Fact]
    public async Task DomainExpiryDuringAuditRollsBackApprovalAndInbox()
    {
        await using var f = await SandboxFixture.Create(); var current = await MiniAppReviewDecisionTests.Started(f);
        await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(1)));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new ExpireProof(f.Clock)).Options);
        var service = new MiniAppReviewDecisionService(db, new(db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f, db: db));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ApproveAsync(current.Admin, current.Session, current.Review.Id,
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, null), default));
        Assert.Equal(MiniAppReviewStatus.InReview, await f.Db.MiniAppReviewRequests.Select(x => x.Status).SingleAsync()); Assert.Equal(2, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
}
