using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewApprovalPostgresTests
{
    [Fact]
    public async Task ConcurrentApprovalsCommitOneDecisionAndNeverPublishAutomatically()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname"); var current = await MiniAppReviewDecisionTests.Started(f);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<MiniAppReviewSummaryDto?> Approve()
        {
            await using var db = new AppDbContext(options); var service = new MiniAppReviewDecisionService(db, new(db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f, db: db));
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task;
            try { return await service.ApproveAsync(current.Admin, current.Session, current.Review.Id, new(Guid.NewGuid(), current.Review.ConcurrencyToken, null), default); }
            catch (DeveloperAuthException error) when (error.Status == 409) { return null; }
        }
        Assert.Single(await Task.WhenAll(Approve(), Approve()), x => x != null);
        Assert.Single(await f.Db.MiniAppReviewActions.Where(x => x.Action == MiniAppReviewActionKind.ReviewApproved).ToListAsync());
        Assert.Equal(MiniAppReviewStatus.Approved, await f.Db.MiniAppReviewRequests.Select(x => x.Status).SingleAsync());
        Assert.Equal(3, await f.Db.MiniAppReviewNotifications.CountAsync()); Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }
}
