using Viora.Application.MiniApps;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class MiniAppCatalogTests
{
    [Fact]
    public async Task ClearingHistoryPersistsAccountCutoffAndPreservesOtherAccountsAndLogs()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input());
        f.Db.MiniAppLaunchLogs.AddRange(
            new MiniAppLaunchLog { AccountId = f.Owner.Id, MiniAppId = app.Id, Status = MiniAppLaunchStatus.Succeeded },
            new MiniAppLaunchLog { AccountId = f.Other.Id, MiniAppId = app.Id, Status = MiniAppLaunchStatus.Succeeded });
        await f.Db.SaveChangesAsync();
        var service = new MiniAppCatalogService(f.Db);
        var auditCount = await f.Db.MiniAppAuditLogs.CountAsync();
        await service.ClearHistoryAsync(f.Owner.Id, default);

        var cutoff = await f.Db.Accounts.Where(account => account.Id == f.Owner.Id)
            .Select(account => account.MiniAppHistoryClearedBefore).SingleAsync();
        Assert.NotNull(cutoff);
        Assert.Null(await f.Db.Accounts.Where(account => account.Id == f.Other.Id)
            .Select(account => account.MiniAppHistoryClearedBefore).SingleAsync());
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "recent"), default)).Items);
        Assert.Single((await service.GetCatalogAsync(f.Other.Id, new(Rail: "recent"), default)).Items);
        Assert.Equal(2, await f.Db.MiniAppLaunchLogs.CountAsync());
        Assert.Equal(auditCount, await f.Db.MiniAppAuditLogs.CountAsync());

        var launch = new MiniAppLaunchLog { AccountId = f.Owner.Id, MiniAppId = app.Id, Status = MiniAppLaunchStatus.Succeeded };
        f.Db.MiniAppLaunchLogs.Add(launch);
        await f.Db.SaveChangesAsync();
        await f.Db.MiniAppLaunchLogs.Where(log => log.Id == launch.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(log => log.CreatedAt, cutoff!.Value.AddSeconds(1)));
        Assert.Single((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "recent"), default)).Items);
    }

    [Fact]
    public async Task InactiveAccountCannotClearHistory()
    {
        await using var f = await Fixture.Create(true);
        f.Owner.DeletedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<MiniAppException>(() => new MiniAppCatalogService(f.Db).ClearHistoryAsync(f.Owner.Id, default));
        Assert.Equal("ACCOUNT_NOT_ACTIVE", error.Code);
        Assert.Null(await f.Db.Accounts.Where(account => account.Id == f.Owner.Id)
            .Select(account => account.MiniAppHistoryClearedBefore).SingleAsync());
    }

    [Fact]
    public async Task OldUsageIsExcludedFromDiscoveryWithoutDeletingAuditRows()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.Input());
        f.Db.MiniAppLaunchLogs.Add(new MiniAppLaunchLog { AccountId = f.Owner.Id, MiniAppId = app.Id, Status = MiniAppLaunchStatus.Succeeded, CreatedAt = DateTime.UtcNow.AddDays(-91) });
        await f.Db.SaveChangesAsync();
        // SaveChanges stamps insertion times; age the persisted fixture after insertion.
        await f.Db.MiniAppLaunchLogs.ExecuteUpdateAsync(update => update.SetProperty(log => log.CreatedAt, DateTime.UtcNow.AddDays(-91)));
        var service = new MiniAppCatalogService(f.Db);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "recent"), default)).Items);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "popular"), default)).Items);
        Assert.Single(f.Db.MiniAppLaunchLogs);
    }
    [Fact]
    public async Task DraftCannotBeListedOrLaunchedEvenIfStatusIsActive()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input());
        var entity = f.Db.MiniApps.Single(item => item.Id == app.Id);
        entity.PublishedVersion = 0; entity.PublishedConfigurationJson = null;
        await f.Db.SaveChangesAsync();
        Assert.Null(await f.Runtime.GetActiveDetailAsync(app.Id, default));
        Assert.Empty((await f.Runtime.GetActiveAsync(default)).Items);
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default));
        Assert.Equal(MiniAppErrorCodes.NotActive, error.Code);
    }
    [Fact]
    public async Task HistoryAndPopularityUseSuccessfulLaunchesAndIsolateAccounts()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input());
        var service = new MiniAppCatalogService(f.Db);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "recent"), default)).Items);
        await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        Assert.Single((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "recent"), default)).Items);
        Assert.Empty((await service.GetCatalogAsync(f.Other.Id, new(Rail: "recent"), default)).Items);
        Assert.Single((await service.GetCatalogAsync(f.Other.Id, new(Rail: "popular"), default)).Items);
        await service.ClearHistoryAsync(f.Owner.Id, default);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "recent"), default)).Items);
        Assert.Single((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "popular"), default)).Items);
    }
    [Fact]
    public async Task CatalogUsesPublishedSnapshotAndHidesSuspendedOrUnpublishedApps()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input());
        await f.Management.UpdateOwnedAsync(f.Owner.Id, app.Id, f.Input() with { Name = "Unreviewed draft" }, default);
        var service = new MiniAppCatalogService(f.Db);
        var catalog = await service.GetCatalogAsync(f.Owner.Id, new(), default);
        Assert.Equal("Example app", Assert.Single(catalog.Items).Name);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Search: "Unreviewed"), default)).Items);
        await f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Suspended, "Maintenance", default);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(), default)).Items);
    }

    [Fact]
    public async Task FavoritesAreIdempotentOwnedAndBlockedAppsDisappear()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input());
        var service = new MiniAppCatalogService(f.Db);
        await service.SetFavoriteAsync(f.Owner.Id, app.Id, true, default);
        await service.SetFavoriteAsync(f.Owner.Id, app.Id, true, default);
        Assert.Single((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "favorites"), default)).Items);
        Assert.Empty((await service.GetCatalogAsync(f.Other.Id, new(Rail: "favorites"), default)).Items);
        Assert.Single(f.Db.MiniAppFavorites);
        await service.SetFavoriteAsync(f.Owner.Id, app.Id, false, default);
        Assert.Empty((await service.GetCatalogAsync(f.Owner.Id, new(Rail: "favorites"), default)).Items);
    }

    [Fact]
    public async Task QueryRejectsInvalidBoundsAndUnknownRails()
    {
        await using var f = await Fixture.Create(true);
        var service = new MiniAppCatalogService(f.Db);
        await Assert.ThrowsAsync<MiniAppException>(() => service.GetCatalogAsync(f.Owner.Id, new(Page: 0), default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.GetCatalogAsync(f.Owner.Id, new(PageSize: 101), default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.GetCatalogAsync(f.Owner.Id, new(Rail: "random"), default));
    }
}
