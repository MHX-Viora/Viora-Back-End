using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionRetentionTests
{
    [Fact]
    public async Task EachCleanupBatchIsBoundedAndReferencedSessionsSurviveUntilTheirEventsExpire()
    {
        await using var f = await ProductionRuntimeFixture.Create();
        // Typed retention history only, not evidence of actual runtime/business events.
        var session = await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        for (var i = 0; i < 105; i++) f.F.Db.Add(new MiniAppProductionRuntimeEvent
        { MiniAppId = session.MiniAppId, HostSessionId = session.Id, CorrelationId = session.CorrelationId, Code = MiniAppProductionEventCode.BridgeReady });
        await f.F.Db.SaveChangesAsync(); f.F.Clock.Now += TimeSpan.FromDays(8);
        var service = new MiniAppProductionRetentionService(f.F.Db, f.F.Clock, MiniAppProductionLaunchTests.Options(), new(f.F.Db));
        var first = await service.CleanupAsync(default); Assert.Equal(100, first.EventsDeleted); Assert.Equal(0, first.SessionsDeleted);
        Assert.Equal(7, await f.F.Db.Set<MiniAppProductionRuntimeEvent>().CountAsync());
        var second = await service.CleanupAsync(default); Assert.Equal(7, second.EventsDeleted); Assert.Equal(1, second.SessionsDeleted);
    }
    [Fact]
    public async Task CleanupExpiresUnusedGrantErasesReceiptAndClosesOnlyItsDormantSession()
    {
        await using var f = await MiniAppProductionLaunchTests.Published(); var provider = new EphemeralDataProtectionProvider();
        var options = MiniAppProductionLaunchTests.Options(); var bindings = new MiniAppProductionBindingService(provider, f.Clock, options); var prepared = bindings.Prepare(null);
        await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher()).LaunchAsync(f.App.AppId,
            MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        var service = new MiniAppProductionRetentionService(f.Db, f.Clock, options, new(f.Db));
        Assert.Equal(0, (await service.CleanupAsync(default)).SessionsClosed);
        f.Clock.Now += TimeSpan.FromSeconds(31);
        var result = await service.CleanupAsync(default);
        Assert.Equal(1, result.GrantsExpired); Assert.Equal(1, result.SessionsClosed); Assert.Equal(0, result.SessionsDeleted);
        var grant = await f.Db.Set<MiniAppProductionLaunchGrant>().AsNoTracking().SingleAsync(); Assert.Null(grant.ReceiptProtected); Assert.NotNull(grant.ConsumedAt);
        Assert.NotNull((await f.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).ClosedAt);
        Assert.Single(await f.Db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Invalidated).ToArrayAsync());
        Assert.Equal(0, (await service.CleanupAsync(default)).SessionsClosed);
    }
    [Fact]
    public async Task RetentionDeletesOnlyExpiredRuntimeHistoryAndPreservesStageSevenAuthorityAndSocialSource()
    {
        await using var f = await ProductionRuntimeFixture.Create();
        f.F.Clock.Now += TimeSpan.FromDays(8);
        var service = new MiniAppProductionRetentionService(f.F.Db, f.F.Clock, MiniAppProductionLaunchTests.Options(), new(f.F.Db));
        var result = await service.CleanupAsync(default);
        Assert.Equal(2, result.EventsDeleted); Assert.Equal(1, result.GrantsDeleted); Assert.Equal(1, result.SessionsDeleted);
        Assert.Empty(await f.F.Db.Set<MiniAppProductionHostSession>().ToArrayAsync());
        Assert.Single(await f.F.Db.MiniAppPublications.ToArrayAsync()); Assert.Single(await f.F.Db.MiniAppReviewRequests.ToArrayAsync());
        Assert.Single(await f.F.Db.DeveloperSessions.ToArrayAsync());
        Assert.False(await f.F.Db.RefreshTokens.AnyAsync(x => x.RevokedAt != null));
    }
    [Fact]
    public async Task DisabledCleanupDoesNotAccessStorage()
    {
        await using var f = await SandboxFixture.Create(); await f.Db.DisposeAsync();
        var result = await new MiniAppProductionRetentionService(f.Db, f.Clock, new(), new(f.Db)).CleanupAsync(default);
        Assert.Equal(0, result.EventsDeleted); Assert.Equal(0, result.SessionsDeleted);
    }
}
