using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoConsentServiceTests
{
    [Fact]
    public async Task PostgreSqlConcurrentApprovalsLeaveOneConsistentActiveGrant()
    {
        await using var postgres = await IsolatedPostgres.Start();
        await using var host = await SsoProtocolTestHost.Create(postgres);
        async Task<SsoGrant?> Approve()
        {
            try
            {
                return await host.WithServices(services => services.GetRequiredService<SsoConsentService>()
                    .GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
            }
            catch (DbUpdateException) { return null; }
            catch (Npgsql.PostgresException e) when (e.SqlState == "40001") { return null; }
        }
        var results = await Task.WhenAll(Approve(), Approve());
        Assert.Contains(results, x => x != null);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Single(await db.MiniAppPairwiseSubjects.ToListAsync());
            var consent = Assert.Single(await db.MiniAppUserConsents.Where(x => x.RevokedAt == null).ToListAsync());
            var grant = await services.GetRequiredService<SsoConsentService>().FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default);
            Assert.NotNull(grant); Assert.Equal(consent.AuthorizationId, grant.AuthorizationId);
            Assert.All(results.Where(x => x != null), x => Assert.Equal(grant.Subject, x!.Subject));
            return true;
        });
    }

    [Fact]
    public async Task ExpiredConsentCanBeRenewedWithoutChangingPairwiseIdentity()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var first = await host.WithServices(services => services.GetRequiredService<SsoConsentService>()
            .GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var expired = await db.MiniAppUserConsents.SingleAsync();
            expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            var service = services.GetRequiredService<SsoConsentService>();
            Assert.Null(await service.FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
            var renewed = await service.GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default);
            Assert.Equal(first.Subject, renewed.Subject);
            var principal = await service.PrincipalAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default);
            Assert.Equal(first.Subject, principal.GetClaim(OpenIddictConstants.Claims.Subject));
            Assert.Equal(2, await db.MiniAppUserConsents.CountAsync());
            Assert.NotNull((await db.MiniAppUserConsents.SingleAsync(x => x.AuthorizationId == first.AuthorizationId)).RevokedAt);
            return true;
        });
    }

    [Fact]
    public async Task GrantsRequireExactOwnerAndScopesWhilePairwiseSubjectSurvivesReconsent()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var first = await host.WithServices(async services =>
        {
            var consents = services.GetRequiredService<SsoConsentService>();
            Assert.Null(await consents.FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
            return await consents.GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "profile"], default);
        });
        Assert.NotEqual(host.AccountId.ToString(), first.Subject);
        await host.WithServices(async services =>
        {
            var consents = services.GetRequiredService<SsoConsentService>();
            Assert.Null(await consents.FindAsync(Guid.NewGuid(), SsoProtocolTestHost.ClientId, ["openid"], default));
            Assert.Equal(first.Subject, (await consents.FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default))!.Subject);
            Assert.Null(await consents.FindAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "email"], default));
            var next = await consents.GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "profile", "email"], default);
            Assert.Equal(first.Subject, next.Subject); Assert.NotEqual(first.AuthorizationId, next.AuthorizationId);
            var principal = await consents.PrincipalAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "email"], default);
            Assert.Equal(first.Subject, principal.GetClaim(OpenIddictConstants.Claims.Subject));
            Assert.Null(principal.GetClaim(OpenIddictConstants.Claims.Email));
            Assert.DoesNotContain(principal.Claims, x => x.Value == host.AccountId.ToString());
            Assert.Equal(SsoClientPolicy.Resource(next.Environment), Assert.Single(principal.GetResources()));
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            Assert.Equal(OpenIddictConstants.Statuses.Revoked, await authorizations.GetStatusAsync((await authorizations.FindByIdAsync(first.AuthorizationId))!));
            return true;
        });
    }

    [Fact]
    public async Task OnlyVerifiedEmailAndGrantedProfileClaimsReachThePrincipal()
    {
        await using var host = await SsoProtocolTestHost.Create();
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var account = await db.Accounts.SingleAsync(); account.EmailVerifiedAt = DateTime.UtcNow;
            db.Users.Add(new() { AccountId = account.Id, DisplayName = "Visible profile" }); await db.SaveChangesAsync();
            var consent = services.GetRequiredService<SsoConsentService>();
            await consent.GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "email", "profile"], default);
            var minimal = await consent.PrincipalAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default);
            Assert.Null(minimal.GetClaim(OpenIddictConstants.Claims.Email)); Assert.Null(minimal.GetClaim(OpenIddictConstants.Claims.Name));
            var shared = await consent.PrincipalAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid", "email", "profile"], default);
            Assert.Equal("protocol-user@example.test", shared.GetClaim(OpenIddictConstants.Claims.Email));
            Assert.Equal("Visible profile", shared.GetClaim(OpenIddictConstants.Claims.Name));
            return true;
        });
    }
}
