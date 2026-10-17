using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoAccountLinkTests
{
    [Fact]
    public async Task CredentialExpiringDuringVerificationFailsWithStructuredDenial()
    {
        await using var host = await SsoProtocolTestHost.Create(); await host.MakeConfidential(Secret);
        var grant = await host.WithServices(s => s.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        await host.WithServices(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>(); var now = DateTimeOffset.UtcNow;
            await db.MiniAppClientSecrets.ExecuteUpdateAsync(x => x.SetProperty(p => p.ExpiresAt, now.UtcDateTime.AddMinutes(1)));
            var clock = new ExpiringCredentialClock(now);
            var links = ActivatorUtilities.CreateInstance<SsoAccountLinkService>(s, new SsoClientReader(db, clock), clock);
            await Assert.ThrowsAsync<DeveloperAuthException>(() => links.IssueAsync(SsoProtocolTestHost.ClientId, Secret, grant.Subject, "partner-owned-123", default));
            return true;
        });
    }
    private sealed class ExpiringCredentialClock(DateTimeOffset now) : TimeProvider
    {
        private int reads;
        public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref reads) == 1 ? now : now.AddMinutes(2);
    }
    private const string Secret = "isolated-account-link-client-secret";
    private static SsoAccountLinkService Links(IServiceProvider services) => ActivatorUtilities.CreateInstance<SsoAccountLinkService>(services);
    private static async Task<(string Subject,string Challenge)> Proof(SsoProtocolTestHost host, string partnerId = "partner-owned-123")
    {
        var grant = await host.WithServices(services => services.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        var proof = await host.WithServices(services => Links(services).IssueAsync(SsoProtocolTestHost.ClientId, Secret, grant.Subject, partnerId, default));
        return (grant.Subject, proof.Challenge);
    }

    [Fact]
    public async Task ProofRequiresConfidentialBackendCurrentSecretAndMatchingLiveConsentOwner()
    {
        await using var host = await SsoProtocolTestHost.Create();
        var grant = await host.WithServices(s => s.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => Links(s).IssueAsync(SsoProtocolTestHost.ClientId, Secret, grant.Subject, "partner-owned-123", default)));
        await host.MakeConfidential(Secret);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => Links(s).IssueAsync(SsoProtocolTestHost.ClientId, "wrong", grant.Subject, "partner-owned-123", default)));
        var proof = await Proof(host);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => Links(s).ConfirmAsync(Guid.NewGuid(), proof.Challenge, default)));
        var linked = await host.WithServices(s => Links(s).ConfirmAsync(host.AccountId, proof.Challenge, default));
        Assert.Equal(proof.Subject, linked.Subject);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => Links(s).ConfirmAsync(host.AccountId, proof.Challenge, default)));
    }

    [Fact]
    public async Task UnlinkAndRelinkPreservePairwiseIdentityAndPreventReassignment()
    {
        await using var host = await SsoProtocolTestHost.Create(); await host.MakeConfidential(Secret);
        var proof = await Proof(host);
        var link = await host.WithServices(s => Links(s).ConfirmAsync(host.AccountId, proof.Challenge, default));
        Assert.False(await host.WithServices(s => Links(s).UnlinkAsync(Guid.NewGuid(), link.Id, default)));
        Assert.True(await host.WithServices(s => Links(s).UnlinkAsync(host.AccountId, link.Id, default)));
        var second = await Proof(host);
        var relinked = await host.WithServices(s => Links(s).ConfirmAsync(host.AccountId, second.Challenge, default));
        Assert.Equal(link.Id, relinked.Id); Assert.Equal(link.Subject, relinked.Subject); Assert.Null(relinked.UnlinkedAt);
        var replacement = await Proof(host, "another-partner-account");
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => Links(s).ConfirmAsync(host.AccountId, replacement.Challenge, default)));
        Assert.Single(await host.WithServices(s => Links(s).ListAsync(host.AccountId, default)));
        Assert.Empty(await host.WithServices(s => Links(s).ListAsync(Guid.NewGuid(), default)));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consent")]
    [InlineData("suspended")]
    [InlineData("domain")]
    [InlineData("environment")]
    [InlineData("credential")]
    public async Task ConfirmationRechecksExpiryConsentAndLifecycle(string change)
    {
        await using var host = await SsoProtocolTestHost.Create(); await host.MakeConfidential(Secret);
        var proof = await Proof(host);
        await host.WithServices(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            if (change == "expired") await db.Set<Viora.Domain.Developers.SsoAccountLinkChallenge>().ExecuteUpdateAsync(x => x.SetProperty(p => p.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            else if (change == "consent") await db.MiniAppUserConsents.ExecuteUpdateAsync(x => x.SetProperty(p => p.RevokedAt, DateTime.UtcNow));
            else if (change == "domain") await db.MiniAppDomainVerifications.ExecuteUpdateAsync(x => x.SetProperty(p => p.RevokedAt, DateTime.UtcNow));
            else if (change == "environment") await db.MiniAppOAuthClients.ExecuteUpdateAsync(x => x.SetProperty(p => p.Environment, Viora.Domain.Developers.MiniAppOAuthEnvironment.Production));
            else if (change == "credential") await db.MiniAppClientSecrets.ExecuteUpdateAsync(x => x.SetProperty(p => p.RevokedAt, DateTime.UtcNow));
            else await host.ChangeAppStatus(Viora.Domain.Developers.MiniAppStatus.Suspended);
            return true;
        });
        await Assert.ThrowsAsync<DeveloperAuthException>(() => host.WithServices(s => Links(s).ConfirmAsync(host.AccountId, proof.Challenge, default)));
    }
}
