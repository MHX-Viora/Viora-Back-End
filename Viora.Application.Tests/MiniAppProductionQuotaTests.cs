using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionQuotaTests
{
    [Fact]
    public async Task ExpiredUnredeemedGrantsStopOccupyingActiveSlots()
    {
        await using var f = await MiniAppProductionLaunchTests.Published(); var provider = new EphemeralDataProtectionProvider();
        var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options()); var browser = bindings.Prepare(null);
        var service = MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher());
        async Task Open()
        {
            var prepared = bindings.Prepare(browser.CookieCredential);
            await service.LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        }
        await Open(); await Open(); await Open();
        Assert.Equal("HOST_SESSION_LIMIT", (await Assert.ThrowsAsync<DeveloperAuthException>(Open)).Code);
        f.Clock.Now += TimeSpan.FromSeconds(31);
        await Open();
        Assert.Equal(4, await f.Db.Set<MiniAppProductionHostSession>().CountAsync());
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 20)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task RecentLaunchRateIsBoundedByBindingAndAuthenticatedAccountEvenAfterClose(bool accountRate, int limit)
    {
        await using var f = await MiniAppProductionLaunchTests.Published(); var provider = new EphemeralDataProtectionProvider();
        var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options()); var prepared = bindings.Prepare(null);
        var binding = bindings.RequireLaunch(prepared.Public.BindingId, prepared.Public.BrowserNonce);
        var actor = MiniAppProductionActor.Anonymous;
        if (accountRate)
        {
            var account = new Account { Email = "launch-quota@example.test" }; var sid = Guid.NewGuid();
            f.Db.AddRange(account, new RefreshToken { AccountId = account.Id, SessionId = sid, TokenHash = "launch-quota-fixture", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) });
            await f.Db.SaveChangesAsync(); actor = new(account.Id, sid);
        }
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        // Unit quota history only; not a substitute for actual business launch E2E.
        for (var i = 0; i < limit; i++)
        {
            var row = MiniAppProductionHostPersistenceTests.Session(target);
            row.BindingId = accountRate ? Guid.NewGuid() : binding.Id;
            row.AccountId = actor.AccountId; row.SourceSocialSessionId = actor.SourceSocialSessionId; f.Db.Add(row);
        }
        await f.Db.SaveChangesAsync();
        // Parameterize the timestamp: SQLite's translated UtcNow truncates to
        // milliseconds and can precede the just-persisted CreatedAt value.
        var closedAt = DateTime.UtcNow;
        await f.Db.Set<MiniAppProductionHostSession>().ExecuteUpdateAsync(s => s.SetProperty(x => x.ClosedAt, closedAt));
        var options = MiniAppProductionLaunchTests.Options(); options.LaunchLimitPerMinute = limit;
        var service = new MiniAppProductionLaunchService(f.Db, f.Clock, options, new(f.Db, f.Clock, options),
            new(f.Db, f.Clock), bindings, new MiniAppReviewReadinessTests.Fetcher(), provider, new(f.Db));
        var error = await Assert.ThrowsAsync<DeveloperAuthException>(() => service.LaunchAsync(f.App.AppId, actor,
            new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default));
        Assert.Equal(429, error.Status); Assert.Equal("HOST_LAUNCH_LIMIT", error.Code);
    }
}
