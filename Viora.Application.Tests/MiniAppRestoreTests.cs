using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppRestoreTests
{
    private sealed class ExpireProof(SandboxClock clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.Restored)) clock.Now += TimeSpan.FromSeconds(2);
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreRechecksSafetyAfterNetworkAndBeforeCommit(bool duringAudit)
    {
        await using var f = await SandboxFixture.Create(); var publication = await MiniAppUnpublishingTests.Published(f); var actor = await MiniAppReviewAdminReadTests.Admin(f);
        var suspended = await MiniAppModerationTests.Service(f).SuspendAsync(actor.Admin, actor.Session, f.App.AppId, MiniAppModerationTests.Command(publication), default);
        await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(1)));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()); if (duringAudit) options.AddInterceptors(new ExpireProof(f.Clock));
        await using var db = new AppDbContext(options.Options);
        var fetcher = new MiniAppReviewReadinessTests.Fetcher { DuringFetch = duringAudit ? null : async () =>
            { await db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime).SetProperty(x => x.Status, MiniAppDomainStatus.Revoked)); } };
        var service = new MiniAppModerationService(db, new(db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f, fetcher, db));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RestoreAsync(actor.Admin, actor.Session, f.App.AppId, Command(suspended), default));
        Assert.Equal(MiniAppOperationalStatus.Suspended, await f.Db.MiniAppOperationalStates.Select(x => x.Status).SingleAsync());
        Assert.Equal(5, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
    internal static MiniAppModerationRequest Command(MiniAppModerationResultDto suspended) => new(Guid.NewGuid(), suspended.AppVersion,
        "Security investigation resolved and live conditions verified.", suspended.Operational.ConcurrencyToken);
    [Fact]
    public async Task RestoreRequiresAdminAndLeavesApprovedTargetUnpublishedUntilExplicitRepublish()
    {
        await using var f = await SandboxFixture.Create(); var publication = await MiniAppUnpublishingTests.Published(f);
        var actor = await MiniAppReviewAdminReadTests.Admin(f); var service = MiniAppModerationTests.Service(f);
        var suspended = await service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, MiniAppModerationTests.Command(publication), default);
        var command = Command(suspended);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RestoreAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default));
        var restored = await service.RestoreAsync(actor.Admin, actor.Session, f.App.AppId, command, default);
        Assert.Equal("Active", restored.Operational.Status); Assert.Null(restored.Publication);
        Assert.Equal(restored, await service.RestoreAsync(actor.Admin, actor.Session, f.App.AppId, command, default));
        Assert.False(await f.Db.MiniAppPublications.AnyAsync(x => x.Status == MiniAppPublicationStatus.Published));
        Assert.False(await f.Db.MiniAppSandboxSessions.AnyAsync(x => x.RevokedAt == null));
        Assert.Equal(6, await f.Db.MiniAppReviewNotifications.CountAsync());
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, publication.Publication.VersionId, default)).Readiness;
        Assert.True(ready.CanPublish);
        var republished = await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        Assert.NotEqual(publication.Publication.Id, republished.Publication.Id);
        Assert.Equal(MiniAppPublicationStatus.Suspended, await f.Db.MiniAppPublications.Where(x => x.Id == publication.Publication.Id).Select(x => x.Status).SingleAsync());
    }
    [Fact]
    public async Task RestoreCannotOverrideExpiredDomainOrStaleOperationalRevision()
    {
        await using var f = await SandboxFixture.Create(); var publication = await MiniAppUnpublishingTests.Published(f);
        var actor = await MiniAppReviewAdminReadTests.Admin(f); var service = MiniAppModerationTests.Service(f);
        var suspended = await service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, MiniAppModerationTests.Command(publication), default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RestoreAsync(actor.Admin, actor.Session, f.App.AppId, Command(suspended) with { OperationalRevision = Guid.NewGuid() }, default));
        await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(-1)));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RestoreAsync(actor.Admin, actor.Session, f.App.AppId, Command(suspended), default));
        Assert.Equal(MiniAppOperationalStatus.Suspended, await f.Db.MiniAppOperationalStates.Select(x => x.Status).SingleAsync());
        Assert.Equal(5, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
}
