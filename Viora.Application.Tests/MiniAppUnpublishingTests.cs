using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppUnpublishingTests
{
    internal static MiniAppUnpublishRequest Command(MiniAppPublishResultDto published) => new(Guid.NewGuid(), published.AppVersion,
        null, published.Publication.Id, published.Publication.ConcurrencyToken, published.OperationalRevision);
    internal static async Task<MiniAppPublishResultDto> Published(SandboxFixture f)
    {
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        return await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
    }
    [Fact]
    public async Task OwnerUnpublishesExactPublicationOnceAndCanExplicitlyRepublishApprovedVersion()
    {
        await using var f = await SandboxFixture.Create(); var published = await Published(f); var request = Command(published); var service = MiniAppPublishingTests.Service(f);
        var result = await service.UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        Assert.Equal("UnpublishedByOwner", result.Publication.Status); Assert.NotNull(result.Publication.UnpublishedAt);
        Assert.Equal(result, await service.UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default));
        Assert.Equal(5, await f.Db.MiniAppReviewNotifications.CountAsync()); Assert.Equal(5, await f.Db.MiniAppReviewNotificationDeliveries.CountAsync());
        Assert.Equal(MiniAppOperationalStatus.Active, await f.Db.MiniAppOperationalStates.Select(x => x.Status).SingleAsync());
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, published.Publication.VersionId, default)).Readiness;
        Assert.True(ready.CanPublish); Assert.False(ready.CanUnpublish);
        var again = await service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        Assert.NotEqual(published.Publication.Id, again.Publication.Id);
        Assert.Equal("UnpublishedByOwner", (await service.UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default)).Publication.Status);
        Assert.Equal(again.Publication.Id, (await f.Db.MiniAppPublications.AsNoTracking().SingleAsync(x => x.Status == MiniAppPublicationStatus.Published)).Id);
    }
    [Fact]
    public async Task ForeignOwnerAndStalePublicationCannotUnpublish()
    {
        await using var f = await SandboxFixture.Create(); var published = await Published(f); var service = MiniAppPublishingTests.Service(f);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.UnpublishAsync(Guid.NewGuid(), f.Source.Id, f.App.AppId, Command(published), default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, Command(published) with { PublicationId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, Command(published) with { PublicationRevision = Guid.NewGuid() }, default));
        Assert.Equal(4, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Equal(MiniAppPublicationStatus.Published, await f.Db.MiniAppPublications.Select(x => x.Status).SingleAsync());
    }
    private sealed class Fail : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.Unpublished))
                throw new InvalidOperationException("Injected audit storage failure");
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Fact]
    public async Task AuditFailureRollsBackUnpublishAndRootRevision()
    {
        await using var f = await SandboxFixture.Create(); var published = await Published(f);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new Fail()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MiniAppPublishingTests.Service(f, db).UnpublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, Command(published), default));
        Assert.Equal(published.AppVersion, await f.Db.MiniApps.Select(x => x.Version).SingleAsync());
        Assert.Equal(MiniAppPublicationStatus.Published, await f.Db.MiniAppPublications.Select(x => x.Status).SingleAsync());
        Assert.Equal(4, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
}
