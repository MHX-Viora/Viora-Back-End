using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewDecisionPostgresTests
{
    [Fact]
    public async Task CompetingTerminalDecisionsHaveOneWinnerAndImmutableHistory()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname"); var current = await MiniAppReviewDecisionTests.Started(f);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<MiniAppReviewSummaryDto?> Decide(bool changes)
        {
            await using var db = new AppDbContext(options); var service = new MiniAppReviewDecisionService(db, new(db, f.Clock), f.Clock);
            var request = new MiniAppReviewDecisionRequest(Guid.NewGuid(), current.Review.ConcurrencyToken, "The page requires a concrete correction",
                changes ? [new("description", "Please add complete product information.")] : null);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task;
            try { return changes ? await service.RequestChangesAsync(current.Admin, current.Session, current.Review.Id, request, default)
                : await service.RejectAsync(current.Admin, current.Session, current.Review.Id, request, default); }
            catch (DeveloperAuthException error) when (error.Status == 409) { return null; }
        }
        Assert.Single(await Task.WhenAll(Decide(false), Decide(true)), x => x != null);
        Assert.Single(await f.Db.MiniAppReviewActions.Where(x => x.Action == MiniAppReviewActionKind.ReviewRejected || x.Action == MiniAppReviewActionKind.ChangesRequested).ToListAsync());
        Assert.Equal(3, await f.Db.MiniAppReviewNotifications.CountAsync());
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppReviewRequests\" SET \"DecisionReason\"='New terminal reason must not replace history.' WHERE \"Id\"='{current.Review.Id}'");
    }
}
