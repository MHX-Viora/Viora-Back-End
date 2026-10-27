using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionEventTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public async Task ClientEventsUseFixedCodesAndBoundedQuotaWithoutGrantingAuthority(int limit)
    {
        await using var f = await ProductionRuntimeFixture.Create();
        var options = MiniAppProductionLaunchTests.Options(); options.EventsPerSessionPerMinute = limit;
        var service = new MiniAppProductionRuntimeService(f.F.Db, f.F.Clock, options, new(f.F.Db, f.F.Clock, options),
            new(f.F.Db, f.F.Clock), f.Bindings, new(f.F.Db));
        var denied = await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RecordEventAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, MiniAppProductionEventCode.Redeemed, default));
        Assert.Equal(400, denied.Status);
        for (var i = 0; i < limit; i++)
            await service.RecordEventAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, MiniAppProductionEventCode.BridgeReady, default);
        Assert.Equal(429, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RecordEventAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, MiniAppProductionEventCode.BridgeReady, default))).Status);
        Assert.Equal(limit, await f.F.Db.Set<MiniAppProductionRuntimeEvent>().CountAsync(x => x.Code == MiniAppProductionEventCode.BridgeReady));
        Assert.Equal(f.F.App.AppId, (await service.ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default)).AppId);

    }
}
