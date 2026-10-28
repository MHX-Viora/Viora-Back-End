using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppPublishingPostgresTests
{
    [Fact]
    public async Task ConcurrentPublishHasOneWinnerAndReplacementClosesImmutableHistory()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname");
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var readiness = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var count = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(MiniAppPublishRequest Command, MiniAppPublishResultDto? Result)> Publish()
        {
            await using var db = new AppDbContext(options); var request = MiniAppPublishingTests.Command(readiness);
            if (Interlocked.Increment(ref count) == 2) start.SetResult(); await start.Task;
            try { return (request, await MiniAppPublishingTests.Service(f, db).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default)); }
            catch (DeveloperAuthException error) when (error.Status == 409) { return (request, null); }
        }
        var winner = Assert.Single(await Task.WhenAll(Publish(), Publish()), x => x.Result != null);
        Assert.Equal(winner.Result, await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, winner.Command, default));
        Assert.Single(await f.Db.MiniAppPublications.ToListAsync()); Assert.Equal(4, await f.Db.MiniAppReviewNotifications.CountAsync());
        foreach (var session in await f.Db.MiniAppSandboxSessions.AsNoTracking().Where(x => x.RevokedAt == null).Select(x => x.Id).ToArrayAsync())
            await f.Service().RevokeAsync(f.Actor, f.App.AppId, session, default);
        await f.Db.Entry(f.App).ReloadAsync(); f.App.Name = "Reviewed replacement";
        var replacement = await MiniAppPublishingReadinessTests.Approved(f);
        var next = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, replacement.VersionId, default)).Readiness;
        var published = await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(next), default);
        Assert.Equal(replacement.VersionId, published.Publication.VersionId);
        Assert.Single(await f.Db.MiniAppPublications.Where(x => x.Status == MiniAppPublicationStatus.Published).ToListAsync());
        var closed = await f.Db.MiniAppPublications.AsNoTracking().SingleAsync(x => x.Id == winner.Result!.Publication.Id);
        Assert.Equal(MiniAppPublicationStatus.Archived, closed.Status); Assert.NotNull(closed.UnpublishedAt);
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppPublications\" SET \"Status\"=0,\"UnpublishedAt\"=NULL WHERE \"Id\"='{closed.Id}'");
        Assert.Equal(8, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
}
