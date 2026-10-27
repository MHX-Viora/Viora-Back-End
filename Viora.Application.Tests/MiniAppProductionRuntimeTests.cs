using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionRuntimeTests
{
    [Fact]
    public async Task ContextRequiresBothPrivateCookiesAndNonceAndCloseIsIdempotentAndScoped()
    {
        await using var f = await ProductionRuntimeFixture.Create(); var service = f.Service();
        Assert.Equal(f.F.App.AppId, (await service.ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default)).AppId);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ContextAsync(f.SessionId, MiniAppProductionCredentials.New(), f.Nonce, f.Cookie, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ContextAsync(f.SessionId, f.Credential, MiniAppProductionCredentials.New(), f.Cookie, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CloseAsync(f.SessionId, f.Credential, f.Nonce, null, default));
        Assert.Null((await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync()).ClosedAt);
        await service.CloseAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default);
        await service.CloseAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default);
        Assert.Equal(410, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default))).Status);
        Assert.Single(await f.F.Db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Closed).ToArrayAsync());
    }
    [Fact]
    public async Task HeartbeatIsBoundedAndNeverExtendsAbsoluteExpiry()
    {
        await using var f = await ProductionRuntimeFixture.Create(); var service = f.Service();
        var before = await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        f.F.Clock.Now += TimeSpan.FromSeconds(16);
        await service.HeartbeatAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default);
        await service.HeartbeatAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default);
        var after = await f.F.Db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        Assert.Equal(before.ExpiresAt, after.ExpiresAt); Assert.True(after.LastActivityAt > before.LastActivityAt);
        Assert.Single(await f.F.Db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Heartbeat).ToArrayAsync());
    }
    [Theory]
    [InlineData("domain")]
    [InlineData("unpublish")]
    [InlineData("developer")]
    public async Task LiveAuthorityChangeHaltsContextAndPersistsOneInvalidation(string change)
    {
        await using var f = await ProductionRuntimeFixture.Create(); var db = f.F.Db;
        if (change == "domain") await db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.F.Clock.Now.UtcDateTime));
        if (change == "unpublish") await db.MiniAppPublications.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppPublicationStatus.UnpublishedByOwner).SetProperty(x => x.UnpublishedAt, DateTime.UtcNow));
        if (change == "developer") await db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DeveloperStatus.Suspended));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service().ContextAsync(f.SessionId, f.Credential, f.Nonce, f.Cookie, default));
        var session = await db.Set<MiniAppProductionHostSession>().AsNoTracking().SingleAsync();
        Assert.NotNull(session.ClosedAt); Assert.Null(session.RuntimeCredentialHash);
        Assert.Single(await db.Set<MiniAppProductionRuntimeEvent>().Where(x => x.Code == MiniAppProductionEventCode.Invalidated).ToArrayAsync());
    }
}

internal sealed class ProductionRuntimeFixture : IAsyncDisposable
{
    public SandboxFixture F { get; private init; } = null!;
    public MiniAppProductionBindingService Bindings { get; private init; } = null!;
    public Guid SessionId { get; private init; }
    public string Credential { get; private init; } = "";
    public string Nonce { get; private init; } = "";
    public string Cookie { get; private init; } = "";
    public MiniAppProductionRuntimeService Service(AppDbContext? database = null)
    {
        var db = database ?? F.Db; var options = MiniAppProductionLaunchTests.Options();
        return new(db, F.Clock, options, new(db, F.Clock, options), new(db, F.Clock), Bindings, new(db));
    }
    public static async Task<ProductionRuntimeFixture> Create(string? connection = null)
    {
        var f = await MiniAppProductionLaunchTests.Published(connection); var provider = new EphemeralDataProtectionProvider();
        var bindings = new MiniAppProductionBindingService(provider, f.Clock, MiniAppProductionLaunchTests.Options()); var prepared = bindings.Prepare(null);
        var receipt = await MiniAppProductionLaunchTests.Service(f, provider, bindings, new MiniAppReviewReadinessTests.Fetcher())
            .LaunchAsync(f.App.AppId, MiniAppProductionActor.Anonymous, new(Guid.NewGuid(), "Web", prepared.Public.BindingId, prepared.Public.BrowserNonce), default);
        f.Clock.Now = DateTimeOffset.UtcNow;
        var issued = await MiniAppProductionGrantTests.Service(f, bindings).RedeemAsync(receipt.Grant, prepared.Public.BrowserNonce, prepared.CookieCredential, default);
        return new() { F = f, Bindings = bindings, SessionId = receipt.SessionId, Credential = issued.Credential, Nonce = prepared.Public.BrowserNonce, Cookie = prepared.CookieCredential };
    }
    public ValueTask DisposeAsync() => F.DisposeAsync();
}
