using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppModerationTests
{
    private sealed class ExpireAdmin(SandboxClock clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.Suspended)) clock.Now += TimeSpan.FromSeconds(2);
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Fact]
    public async Task AdminExpiryDuringAuditRollsBackPublicationAndSandboxRevocation()
    {
        await using var f = await SandboxFixture.Create(); var publication = await MiniAppUnpublishingTests.Published(f); var actor = await MiniAppReviewAdminReadTests.Admin(f);
        await f.Db.RefreshTokens.Where(x => x.AccountId == actor.Admin).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime.AddSeconds(1)));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new ExpireAdmin(f.Clock)).Options);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f, db).SuspendAsync(actor.Admin, actor.Session, f.App.AppId, Command(publication), default));
        Assert.Equal(MiniAppPublicationStatus.Published, await f.Db.MiniAppPublications.Select(x => x.Status).SingleAsync());
        Assert.Equal(MiniAppOperationalStatus.Active, await f.Db.MiniAppOperationalStates.Select(x => x.Status).SingleAsync());
        Assert.True(await f.Db.MiniAppSandboxSessions.AnyAsync(x => x.RevokedAt == null)); Assert.Equal(4, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
    internal static MiniAppModerationService Service(SandboxFixture f, AppDbContext? db = null)
    { db ??= f.Db; return new(db, new(db, f.Clock), f.Clock, MiniAppReviewReadinessTests.Service(f, db: db)); }
    internal static MiniAppModerationRequest Command(MiniAppPublishResultDto publication) => new(Guid.NewGuid(), publication.AppVersion,
        "Application violates the platform safety requirements.", publication.OperationalRevision, publication.Publication.Id, publication.Publication.ConcurrencyToken);

    [Fact]
    public async Task LiveAdminSuspendsPublicationAndSandboxOnceWithoutSandboxTesterRole()
    {
        await using var f = await SandboxFixture.Create(); var publication = await MiniAppUnpublishingTests.Published(f);
        var actor = await MiniAppReviewAdminReadTests.Admin(f); var command = Command(publication); var service = Service(f);
        var result = await service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, command, default);
        Assert.Equal("Suspended", result.Operational.Status); Assert.Equal("Suspended", result.Publication!.Status);
        Assert.Equal(result, await service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, command, default));
        Assert.Equal(MiniAppStatus.Suspended, await f.Db.MiniApps.Select(x => x.Status).SingleAsync());
        Assert.Equal(5, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.False(await f.Db.MiniAppSandboxSessions.AnyAsync(x => x.MiniAppId == f.App.Id && x.RevokedAt == null));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service().CreateAsync(f.Actor, f.App.AppId, default));
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppStatus.Draft));
        // Editing working metadata cannot override the separate operational state.
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service().CreateAsync(f.Actor, f.App.AppId, default));
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, publication.Publication.VersionId, default)).Readiness;
        Assert.False(ready.CanPublish); Assert.Contains("APP_SUSPENDED", ready.BlockingReasons);
    }

    [Fact]
    public async Task OwnerOrRevokedAdminOrStaleRevisionsCannotSuspend()
    {
        await using var f = await SandboxFixture.Create(); var publication = await MiniAppUnpublishingTests.Published(f);
        var actor = await MiniAppReviewAdminReadTests.Admin(f); var command = Command(publication); var service = Service(f);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.SuspendAsync(f.Actor.Id, f.Source.Id, f.App.AppId, command, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, command with { ConcurrencyToken = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, command with { Reason = "short" }, default));
        await f.Db.RefreshTokens.Where(x => x.AccountId == actor.Admin).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.SuspendAsync(actor.Admin, actor.Session, f.App.AppId, command, default));
        Assert.Equal(4, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Equal(MiniAppPublicationStatus.Published, await f.Db.MiniAppPublications.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task AdminCanSuspendAnUnpublishedAppWithoutCreatingAReviewOrPublication()
    {
        await using var f = await SandboxFixture.Create(); var actor = await MiniAppReviewAdminReadTests.Admin(f);
        var result = await Service(f).SuspendAsync(actor.Admin, actor.Session, f.App.AppId,
            new(Guid.NewGuid(), f.App.Version, "Pre-publication security investigation required."), default);
        Assert.Null(result.Publication); Assert.Equal("Suspended", result.Operational.Status);
        Assert.Empty(await f.Db.MiniAppReviewRequests.ToListAsync()); Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
        Assert.Single(await f.Db.MiniAppReviewNotifications.ToListAsync());
    }
}
