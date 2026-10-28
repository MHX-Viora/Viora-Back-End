using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppModerationPostgresTests
{
    [Fact]
    public async Task SuspendAndPublishRaceCannotPublishASuspendedAppAndRestoreCannotReopenHistory()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname"); var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var actor = await MiniAppReviewAdminReadTests.Admin(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var count = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Change(bool suspend)
        {
            await using var db = new AppDbContext(options);
            if (Interlocked.Increment(ref count) == 2) start.SetResult(); await start.Task;
            try
            {
                if (suspend) await MiniAppModerationTests.Service(f, db).SuspendAsync(actor.Admin, actor.Session, f.App.AppId,
                    new(Guid.NewGuid(), ready.AppVersion, "Concurrent administrator security suspension."), default);
                else await MiniAppPublishingTests.Service(f, db).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
                return true;
            }
            catch (DeveloperAuthException e) when (e.Status == 409) { return false; }
        }
        Assert.Single(await Task.WhenAll(Change(false), Change(true)), x => x);
        var state = await f.Db.MiniAppOperationalStates.AsNoTracking().SingleAsync();
        if (state.Status == MiniAppOperationalStatus.Active)
        {
            var publication = await f.Db.MiniAppPublications.AsNoTracking().SingleAsync(); var revision = await f.Db.MiniApps.Select(x => x.Version).SingleAsync();
            await MiniAppModerationTests.Service(f).SuspendAsync(actor.Admin, actor.Session, f.App.AppId,
                new(Guid.NewGuid(), revision, "Rechecked administrator security suspension.", state.ConcurrencyToken, publication.Id, publication.ConcurrencyToken), default);
        }
        var current = await f.Db.MiniAppOperationalStates.AsNoTracking().SingleAsync(); var root = await f.Db.MiniApps.AsNoTracking().SingleAsync();
        Assert.Equal(MiniAppOperationalStatus.Suspended, current.Status); Assert.False(await f.Db.MiniAppPublications.AnyAsync(x => x.Status == MiniAppPublicationStatus.Published));
        var command = new MiniAppModerationRequest(Guid.NewGuid(), root.Version, "Operational conditions rechecked for restoration.", current.ConcurrencyToken);
        var restored = await MiniAppModerationTests.Service(f).RestoreAsync(actor.Admin, actor.Session, f.App.AppId, command, default);
        Assert.Equal(restored, await MiniAppModerationTests.Service(f).RestoreAsync(actor.Admin, actor.Session, f.App.AppId, command, default));
        Assert.False(await f.Db.MiniAppPublications.AnyAsync(x => x.Status == MiniAppPublicationStatus.Published));
        Assert.Equal(MiniAppOperationalStatus.Active, await f.Db.MiniAppOperationalStates.Select(x => x.Status).SingleAsync());
    }
}
