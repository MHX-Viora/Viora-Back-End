using Viora.Application.MiniApps;
using Viora.Infrastructure.MiniApps;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class MiniAppEditorialTests
{
    [Fact]
    public async Task BannersRequireSafeInternalTargetAndRespectScheduleAndVisibility()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input()); var service = new MiniAppEditorialService(f.Db);
        var input = new MiniAppBannerInput("Featured", "https://example.com/banner.png", app.Id, null, 1, true, DateTime.UtcNow.AddMinutes(-1), null);
        await Assert.ThrowsAsync<MiniAppException>(() => service.SaveBannerAsync(f.Other.Id, null, input with { ImageUrl = "javascript:alert(1)" }, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.SaveBannerAsync(f.Other.Id, null, input with { ImageUrl = "https://127.0.0.1/private" }, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.SaveBannerAsync(f.Other.Id, null, input with { CategoryId = Guid.NewGuid() }, default));
        var banner = await service.SaveBannerAsync(f.Other.Id, null, input, default);
        Assert.Single(await service.BannersAsync(false, default));
        await service.SaveBannerAsync(f.Other.Id, banner.Id, input with { StartsAt = DateTime.UtcNow.AddDays(1) }, default);
        Assert.Empty(await service.BannersAsync(false, default));
        await service.SaveBannerAsync(f.Other.Id, banner.Id, input, default);
        await service.SetVisibilityAsync(f.Other.Id, app.Id, true, default);
        Assert.Empty(await service.BannersAsync(false, default));
        Assert.Empty((await new MiniAppCatalogService(f.Db).GetCatalogAsync(f.Owner.Id, new(), default)).Items);
        Assert.Single(await service.BannersAsync(true, default));
    }

    [Fact]
    public async Task EditorialPlacementDoesNotDependOnUnreviewedDeveloperFeaturedFlag()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.Input());
        var editorial = new MiniAppEditorialService(f.Db); var catalog = new MiniAppCatalogService(f.Db);
        await f.Management.UpdateOwnedAsync(f.Owner.Id, app.Id, f.Input() with { IsFeatured = true }, default);
        Assert.Empty((await catalog.GetCatalogAsync(f.Owner.Id, new(Rail: "featured"), default)).Items);
        await editorial.SavePlacementAsync(f.Other.Id, app.Id, new(1, true, DateTime.UtcNow.AddMinutes(-1), null), default);
        Assert.Single((await catalog.GetCatalogAsync(f.Owner.Id, new(Rail: "featured"), default)).Items);
    }
}
