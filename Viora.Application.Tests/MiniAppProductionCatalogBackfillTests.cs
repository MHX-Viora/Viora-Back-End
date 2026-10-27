using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionCatalogBackfillTests
{
    [Fact]
    public async Task ReconciliationDefaultsToDryRunAndAddsOnlyMissingProjectionForActualPublishedTarget()
    {
        await using var f = await SandboxFixture.Create();
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        var actions = await f.Db.MiniAppReviewActions.CountAsync();
        var service = new MiniAppCatalogReconcileService(f.Db, f.Clock);
        var report = await service.ReconcileAsync(f.App.AppId, apply: false, default);
        Assert.Equal("ready", report.Status); Assert.False(report.Written);
        Assert.Empty(await f.Db.Set<MiniAppCatalogPublication>().ToArrayAsync());
        Assert.True((await service.ReconcileAsync(f.App.AppId, apply: true, default)).Written);
        Assert.Equal("already-present", (await service.ReconcileAsync(f.App.AppId, apply: true, default)).Status);
        Assert.Single(await f.Db.Set<MiniAppCatalogPublication>().ToArrayAsync());
        Assert.Single(await f.Db.MiniAppPublications.ToArrayAsync());
        Assert.Single(await f.Db.MiniAppReviewRequests.ToArrayAsync());
        Assert.Equal(actions, await f.Db.MiniAppReviewActions.CountAsync());
        await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime)
            .SetProperty(x => x.Status, MiniAppDomainStatus.Revoked));
        Assert.Equal("unavailable", (await service.ReconcileAsync(f.App.AppId, apply: true, default)).Status);
    }

    [Fact]
    public async Task LegacyStatusNeverBecomesAnApprovalOrPublication()
    {
        await using var f = await SandboxFixture.Create();
        f.App.Status = MiniAppStatus.Published; await f.Db.SaveChangesAsync();
        var result = await new MiniAppCatalogReconcileService(f.Db, f.Clock).ReconcileAsync(f.App.AppId, true, default);
        Assert.Equal("unavailable", result.Status); Assert.False(result.Written);
        Assert.Empty(await f.Db.Set<MiniAppCatalogPublication>().ToArrayAsync());
        Assert.Empty(await f.Db.MiniAppPublications.ToArrayAsync());
        Assert.Empty(await f.Db.MiniAppReviewRequests.ToArrayAsync());
    }
}
