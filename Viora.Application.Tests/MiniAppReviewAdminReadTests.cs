using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewAdminReadTests
{
    [Fact]
    public async Task AppViewsFilterBeforeCountAndPagingAndReturnOperationalCapabilities()
    {
        await using var f = await SandboxFixture.Create(); var actor = await Admin(f);
        var service = new MiniAppReviewService(f.Db, new(f.Db, f.Clock));
        Assert.Equal(0, (await service.ListAdminAppsAsync(actor.Admin, actor.Session, null, 1, 20, default, "published")).Total);
        Assert.Equal(0, (await service.ListAdminAppsAsync(actor.Admin, actor.Session, null, 1, 20, default, "suspended")).Total);
        var app = Assert.Single((await service.ListAdminAppsAsync(actor.Admin, actor.Session, null, 1, 20, default)).Items);
        Assert.True(app.CanSuspend); Assert.False(app.CanRestore);
        f.Db.MiniAppOperationalStates.Add(new() { MiniAppId = f.App.Id, Status = MiniAppOperationalStatus.Suspended,
            Reason = "Controlled fixture suspension", SuspendedAt = f.Clock.Now.UtcDateTime, ChangedByAdminAccountId = actor.Admin });
        await f.Db.SaveChangesAsync();
        var page = await service.ListAdminAppsAsync(actor.Admin, actor.Session, null, 1, 20, default, "suspended");
        app = Assert.Single(page.Items); Assert.Equal(1, page.Total); Assert.False(app.CanSuspend); Assert.True(app.CanRestore);
        Assert.Equal("Controlled fixture suspension", app.OperationalReason);
        Assert.Empty((await service.ListAdminAppsAsync(actor.Admin, actor.Session, "missing", 1, 20, default, "suspended")).Items);
        Assert.Equal(400, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ListAdminAppsAsync(actor.Admin, actor.Session, null, 1, 20, default, "sql"))).Status);
    }
    internal static async Task<(Guid Admin, Guid Session)> Admin(SandboxFixture f)
    {
        var admin = new Account { Email = "review-reader-" + Guid.NewGuid().ToString("N") + "@example.test", Role = AccountRole.Admin }; var session = Guid.NewGuid();
        f.Db.Add(new RefreshToken { Account = admin, SessionId = session, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) });
        await f.Db.SaveChangesAsync(); return (admin.Id, session);
    }
    [Fact]
    public async Task LiveAdminReadsFrozenQueueContactOverviewAndCapabilitiesWithoutSandboxTesterAccess()
    {
        await using var f = await SandboxFixture.Create(); var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        var actor = await Admin(f); var service = new MiniAppReviewService(f.Db, new(f.Db, f.Clock));
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Changed working name"));
        var queue = await service.ListAdminAsync(actor.Admin, actor.Session, new(null, "PendingReview", null, "oldest", 1, 20), default);
        var item = Assert.Single(queue.Items); Assert.Equal("Fixture", item.AppName); Assert.Equal(f.App.Developer.EmailNormalized, item.DeveloperEmail);
        Assert.Equal(f.App.LogoUrl, item.LogoUrl);
        var detail = await service.GetAdminAsync(actor.Admin, actor.Session, review.Id, default);
        Assert.True(detail.CanStart); Assert.False(detail.CanDecide); Assert.Null(detail.AssignedAdminAccountId);
        Assert.Empty(f.Options.AdminTesterIds);
        Assert.Equal(1, (await service.OverviewAdminAsync(actor.Admin, actor.Session, default)).Reviews["PendingReview"]);
        var apps = await service.ListAdminAppsAsync(actor.Admin, actor.Session, null, 1, 20, default);
        Assert.Equal(f.App.AppId, Assert.Single(apps.Items).AppId);
        var appDetail = await service.GetAdminAppAsync(actor.Admin, actor.Session, f.App.AppId, default);
        Assert.Single(appDetail.Versions); Assert.Empty(appDetail.Publications); Assert.Null(appDetail.PublishedSnapshot);
        await f.Db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        Assert.Equal(403, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.GetAdminAsync(actor.Admin, actor.Session, review.Id, default))).Status);
    }
    [Fact]
    public async Task QueueRejectsUnsupportedFiltersAndUnboundedPages()
    {
        await using var f = await SandboxFixture.Create(); var actor = await Admin(f); var service = new MiniAppReviewService(f.Db, new(f.Db, f.Clock));
        foreach (var query in new[] { new MiniAppReviewAdminQuery(null, "Everything", null, "oldest", 1, 20), new(null, null, null, "sql", 1, 20),
            new(null, null, null, "oldest", 1, 101), new(new string('x', 129), null, null, "oldest", 1, 20) })
            Assert.Equal(400, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ListAdminAsync(actor.Admin, actor.Session, query, default))).Status);
    }
}
