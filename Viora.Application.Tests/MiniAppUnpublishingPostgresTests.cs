using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppUnpublishingPostgresTests
{
    [Fact]
    public async Task ConcurrentUnpublishClosesOnePublicationAndQueuesOneOwnerEvent()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname"); var publication = await MiniAppUnpublishingTests.Published(f);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(MiniAppUnpublishRequest Command, MiniAppPublishResultDto? Result)> Unpublish()
        {
            await using var db = new AppDbContext(options); var command = MiniAppUnpublishingTests.Command(publication);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task;
            try { return (command, await MiniAppPublishingTests.Service(f, db).UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default)); }
            catch (DeveloperAuthException e) when (e.Status == 409) { return (command, null); }
        }
        var winner = Assert.Single(await Task.WhenAll(Unpublish(), Unpublish()), x => x.Result != null);
        Assert.Equal(winner.Result, await MiniAppPublishingTests.Service(f).UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, winner.Command, default));
        Assert.Equal(MiniAppPublicationStatus.UnpublishedByOwner, await f.Db.MiniAppPublications.Select(x => x.Status).SingleAsync());
        Assert.Equal(5, await f.Db.MiniAppReviewNotifications.CountAsync()); Assert.Equal(5, await f.Db.MiniAppReviewNotificationDeliveries.CountAsync());
    }
}
