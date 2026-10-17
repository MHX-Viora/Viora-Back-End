using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Domain.Developers;

public sealed class SsoAuthorizationOwnerTests
{
    [Fact]
    public async Task PostgreSqlRevocationRollsBackLibraryChangesWhenConsentWriteFails()
    {
        await using var postgres = await IsolatedPostgres.Start();
        var failure = new RejectConsentWrite();
        await using var host = await SsoProtocolTestHost.Create(postgres, options => options.AddInterceptors(failure));
        var exchanged = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host));
        Assert.Equal(System.Net.HttpStatusCode.OK, exchanged.StatusCode);
        var consent = await host.WithServices(services => services.GetRequiredService<AppDbContext>().MiniAppUserConsents.AsNoTracking().SingleAsync());
        async Task<SortedDictionary<string,string?>> TokenState(IServiceProvider services)
        {
            var manager = services.GetRequiredService<IOpenIddictTokenManager>();
            var state = new SortedDictionary<string,string?>();
            await foreach (var token in manager.FindByAuthorizationIdAsync(consent.AuthorizationId!))
                state[(await manager.GetIdAsync(token))!] = await manager.GetStatusAsync(token);
            return state;
        }
        var before = await host.WithServices(TokenState);
        Assert.Contains(OpenIddictConstants.Statuses.Valid, before.Values);
        failure.Armed = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => host.WithServices(services => services.GetRequiredService<SsoAuthorizationOwnerService>()
            .RevokeAsync(host.AccountId, consent.Id, default)));
        failure.Armed = false;
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Null((await db.MiniAppUserConsents.SingleAsync()).RevokedAt);
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            Assert.Equal(OpenIddictConstants.Statuses.Valid, await authorizations.GetStatusAsync((await authorizations.FindByIdAsync(consent.AuthorizationId!))!));
            Assert.Equal(before.ToArray(), (await TokenState(services)).ToArray());
            return true;
        });
        Assert.True(await host.WithServices(services => services.GetRequiredService<SsoAuthorizationOwnerService>().RevokeAsync(host.AccountId, consent.Id, default)));
    }

    [Fact]
    public async Task PostgreSqlConcurrentOwnerRevocationsLeaveNoUsableGrantOrTokens()
    {
        await using var postgres = await IsolatedPostgres.Start();
        await using var host = await SsoProtocolTestHost.Create(postgres);
        var exchanged = await SsoTokenEndpointTests.Exchange(host, await SsoTokenEndpointTests.Code(host));
        Assert.Equal(System.Net.HttpStatusCode.OK, exchanged.StatusCode);
        var consent = await host.WithServices(services => services.GetRequiredService<AppDbContext>().MiniAppUserConsents.AsNoTracking().SingleAsync());
        async Task<bool> Revoke()
        {
            try { return await host.WithServices(services => services.GetRequiredService<SsoAuthorizationOwnerService>().RevokeAsync(host.AccountId, consent.Id, default)); }
            catch (DbUpdateException) { return false; }
            catch (Npgsql.PostgresException e) when (e.SqlState is "40001" or "40P01") { return false; }
        }
        Assert.Contains(true, await Task.WhenAll(Revoke(), Revoke()));
        await host.WithServices(async services =>
        {
            Assert.NotNull((await services.GetRequiredService<AppDbContext>().MiniAppUserConsents.SingleAsync()).RevokedAt);
            Assert.Null(await services.GetRequiredService<SsoConsentService>().FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            await foreach (var token in tokens.FindByAuthorizationIdAsync(consent.AuthorizationId!))
                Assert.Equal(OpenIddictConstants.Statuses.Revoked, await tokens.GetStatusAsync(token));
            return true;
        });
    }

    private sealed class RejectConsentWrite : SaveChangesInterceptor
    {
        public bool Armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<MiniAppUserConsent>().Any(x => x.State == EntityState.Modified))
                throw new DbUpdateException("Isolated test consent write failure.");
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task OwnerHistoryAndRevocationAreIsolatedAndInvalidateTheGrant()
    {
        await using var host = await SsoProtocolTestHost.Create();
        await host.WithServices(async services =>
        {
            var consents = services.GetRequiredService<SsoConsentService>();
            await consents.GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default);
            var current = await consents.GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "profile"], default);
            var owners = services.GetRequiredService<SsoAuthorizationOwnerService>();
            var history = await owners.ListAsync(host.AccountId, default);
            Assert.Equal(2, history.Count); Assert.Single(history, x => x.RevokedAt == null);
            Assert.Empty(await owners.ListAsync(Guid.NewGuid(), default));
            var active = Assert.Single(history, x => x.RevokedAt == null);
            Assert.Null(await owners.GetAsync(Guid.NewGuid(), active.Id, default));
            Assert.False(await owners.RevokeAsync(Guid.NewGuid(), active.Id, default));
            Assert.NotNull(await consents.FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
            Assert.True(await owners.RevokeAsync(host.AccountId, active.Id, default));
            Assert.True(await owners.RevokeAsync(host.AccountId, active.Id, default));
            Assert.Null(await consents.FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
            var manager = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            Assert.Equal(OpenIddictConstants.Statuses.Revoked, await manager.GetStatusAsync((await manager.FindByIdAsync(current.AuthorizationId))!));
            return true;
        });
    }
}
