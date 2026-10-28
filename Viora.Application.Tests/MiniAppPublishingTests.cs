using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppPublishingTests
{
    internal static MiniAppPublishingService Service(SandboxFixture f, AppDbContext? db = null)
    {
        db ??= f.Db;
        return new(db, new(db, f.Clock), MiniAppReviewReadinessTests.Service(f, db: db), f.Clock);
    }
    internal static MiniAppPublishRequest Command(MiniAppPublicationReadinessDto ready) =>
        new(Guid.NewGuid(), ready.AppVersion, ready.VersionId, ready.PublicationId, ready.PublicationRevision, ready.OperationalRevision);

    [Fact]
    public async Task PublishIsExplicitAtomicAndReplaysOnlyItsOriginalPayload()
    {
        await using var f = await SandboxFixture.Create(); var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        var command = Command(ready); var service = Service(f);
        var result = await service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default);
        Assert.Equal("Published", result.Publication.Status); Assert.Equal(approved.VersionId, result.Publication.VersionId);
        Assert.NotEqual(ready.AppVersion, result.AppVersion);
        Assert.Equal(result, await service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default));
        Assert.Single(await f.Db.MiniAppPublications.ToListAsync()); Assert.Equal(4, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Equal(4, await f.Db.MiniAppReviewNotificationDeliveries.CountAsync());
        Assert.Equal(MiniAppOperationalStatus.Active, await f.Db.MiniAppOperationalStates.Select(x => x.Status).SingleAsync());
        Assert.Equal(MiniAppStatus.Published, await f.Db.MiniApps.Select(x => x.Status).SingleAsync());
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command with { VersionId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command with { RequestId = Guid.NewGuid() }, default));
    }

    [Fact]
    public async Task PublishRejectsStaleRevisionAndUnapprovedTargetWithoutSideEffects()
    {
        await using var f = await SandboxFixture.Create(); var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, Command(ready) with { ConcurrencyToken = Guid.NewGuid() }, default));
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync()); Assert.Empty(await f.Db.MiniAppOperationalStates.ToListAsync());
        var revision = await f.Db.MiniApps.Select(x => x.Version).SingleAsync();
        var draft = await MiniAppVersionTests.Service(f).CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), revision, "2.0"), default);
        var root = await f.Db.MiniApps.Select(x => x.Version).SingleAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), root, draft.Id), default));
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }

    private sealed class Expire(SandboxClock clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.Published)) clock.Now += TimeSpan.FromSeconds(2);
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Fact]
    public async Task ProofExpiryDuringAuditRollsBackPublicationStateAndOutbox()
    {
        await using var f = await SandboxFixture.Create(); var approved = await MiniAppPublishingReadinessTests.Approved(f);
        await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(1)));
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new Expire(f.Clock)).Options);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f, db).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, Command(ready), default));
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync()); Assert.Empty(await f.Db.MiniAppOperationalStates.ToListAsync());
        Assert.Equal(3, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Equal(ready.AppVersion, await f.Db.MiniApps.Select(x => x.Version).SingleAsync());
    }
}
