using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionCatalogProjectionTests
{
    [Fact]
    public async Task ActualPublishCreatesFrozenProjectionInItsTransactionAndReplayDoesNotDuplicate()
    {
        await using var f = await SandboxFixture.Create();
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var readiness = MiniAppReviewReadinessTests.Service(f);
        var ready = (await readiness.PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        var command = MiniAppPublishingTests.Command(ready);
        var service = new MiniAppPublishingService(f.Db, new(f.Db, f.Clock), readiness, f.Clock,
            catalog: new MiniAppCatalogProjection(f.Db, new() { Enabled = true }));
        var result = await service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default);
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Later mutable draft"));
        Assert.Equal(result, await service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default));
        var row = await f.Db.Set<MiniAppCatalogPublication>().AsNoTracking().SingleAsync();
        var snapshot = await f.Db.MiniAppVersions.Where(x => x.Id == row.MiniAppVersionId).SingleAsync();
        Assert.Equal(MiniAppVersionSnapshotPolicy.Read(snapshot.SnapshotJson, snapshot.SnapshotHash).Snapshot.Name, row.Name);
        Assert.Equal(result.Publication.Id, row.PublicationId);
        Assert.NotEqual("Later mutable draft", row.Name);
    }

    [Fact]
    public async Task ProjectionFailureRollsBackPublicationAuditNotificationsAndRootRevision()
    {
        await using var f = await SandboxFixture.Create();
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection())
            .AddInterceptors(new FailProjection()).Options);
        var service = new MiniAppPublishingService(db, new(db, f.Clock), MiniAppReviewReadinessTests.Service(f, db: db), f.Clock,
            catalog: new MiniAppCatalogProjection(db, new() { Enabled = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default));
        Assert.Empty(await f.Db.MiniAppPublications.ToArrayAsync());
        Assert.Empty(await f.Db.Set<MiniAppCatalogPublication>().ToArrayAsync());
        Assert.Equal(ready.AppVersion, await f.Db.MiniApps.Select(x => x.Version).SingleAsync());
        Assert.Equal(3, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.False(await f.Db.MiniAppReviewActions.AnyAsync(x => x.Action == MiniAppReviewActionKind.Published));
    }

    private sealed class FailProjection : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<MiniAppCatalogPublication>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Fixture catalog write failure.");
            return base.SavingChangesAsync(data, result, ct);
        }
    }
}
