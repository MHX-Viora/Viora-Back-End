using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class MiniAppProductionCatalogPostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgreSqlFiltersLiveProofAndClientBeforeCountAndPage(bool sso)
    {
        await using var pg = await IsolatedPostgres.Start();
        // Provider/query test data, not business E2E approval evidence.
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var first = await MiniAppProductionCatalogTests.AddUnitPublication(f, 'a', sso);
        var removed = await MiniAppProductionCatalogTests.AddUnitPublication(f, 'b', sso);
        var newest = await MiniAppProductionCatalogTests.AddUnitPublication(f, 'c', sso);
        await f.Db.MiniAppDomainVerifications.Where(x => x.MiniAppId == removed.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime));
        var service = MiniAppProductionCatalogTests.Service(f);
        var page = await service.ListAsync(new(1, 1), default);
        Assert.Equal(2, page.Total);
        Assert.Equal(newest.AppId, Assert.Single(page.Items).Id);
        Assert.Equal(first.AppId, Assert.Single((await service.ListAsync(new(2, 1), default)).Items).Id);
        Assert.Empty((await service.ListAsync(new(3, 1), default)).Items);
        Assert.True((await service.DetailAsync(newest.AppId, default)).LoginAvailable);

    }
}
