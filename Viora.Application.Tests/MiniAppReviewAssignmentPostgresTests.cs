using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewAssignmentPostgresTests
{
    [Fact]
    public async Task TwoAdminsCannotClaimTheSamePendingReview()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname");
        var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        var actors = new[] { await MiniAppReviewAdminReadTests.Admin(f), await MiniAppReviewAdminReadTests.Admin(f) };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<MiniAppReviewSummaryDto?> Claim((Guid Admin, Guid Session) actor)
        {
            await using var db = new AppDbContext(options); var service = new MiniAppReviewDecisionService(db, new(db, f.Clock), f.Clock);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task;
            try { return await service.StartAsync(actor.Admin, actor.Session, review.Id, new(Guid.NewGuid(), review.ConcurrencyToken), default); }
            catch (DeveloperAuthException error) when (error.Status == 409) { return null; }
        }
        Assert.Single(await Task.WhenAll(actors.Select(Claim)), x => x != null);
        Assert.Single(await f.Db.MiniAppReviewActions.Where(x => x.Action == MiniAppReviewActionKind.ReviewStarted).ToListAsync());
        Assert.Equal(2, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Contains((await f.Db.MiniAppReviewRequests.AsNoTracking().SingleAsync()).AssignedAdminAccountId, actors.Select(x => (Guid?)x.Admin));
    }
}
