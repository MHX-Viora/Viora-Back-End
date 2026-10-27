using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewSubmissionPostgresTests
{
    [Fact]
    public async Task ConcurrentSubmissionsSealOneVersionAndOneReviewEventWithDatabaseImmutability()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname"); var request = await MiniAppReviewSubmissionTests.Request(f);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(MiniAppReviewSubmitRequest Request, MiniAppReviewSummaryDto? Result)> Submit()
        {
            await using var db = new AppDbContext(options); var command = request with { RequestId = Guid.NewGuid() };
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task;
            try { return (command, await MiniAppReviewSubmissionTests.Service(f, db).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default)); }
            catch (DeveloperAuthException error) when (error.Status == 409) { return (command, null); }
        }
        var winner = Assert.Single(await Task.WhenAll(Submit(), Submit()), x => x.Result != null);
        Assert.Equal(winner.Result, await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, winner.Request, default));
        Assert.Single(await f.Db.MiniAppReviewRequests.ToListAsync()); Assert.Single(await f.Db.MiniAppReviewNotifications.ToListAsync());
        Assert.Single(await f.Db.MiniAppReviewNotificationDeliveries.ToListAsync());
        Assert.Single(await f.Db.MiniAppReviewActions.Where(x => x.Action == MiniAppReviewActionKind.ReviewSubmitted).ToListAsync());
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppVersions\" SET \"VersionName\"='tamper' WHERE \"Id\"='{request.VersionId}'");
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppReviewRequests\" SET \"EvidenceAttestationJson\"='[]' WHERE \"Id\"='{winner.Result!.Id}'");
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }
}
