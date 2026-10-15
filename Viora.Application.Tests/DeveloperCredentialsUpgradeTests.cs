using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;

public sealed class DeveloperCredentialsUpgradeTests
{
    [Fact]
    public async Task RotationGraceAcceptsBothUntilExpiryAndRevocationPreservesCurrent()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.SsoInput(), default);
        var next = await f.Management.RotateSecretAsync(f.Owner.Id, created.Id, 60, default);
        var app = await f.Db.MiniApps.FindAsync(created.Id);
        var policy = new ClientCredentialService();
        Assert.True(MiniAppSecretPolicy.Verify(policy, app!, created.ClientSecret!, DateTime.UtcNow));
        Assert.True(MiniAppSecretPolicy.Verify(policy, app!, next.ClientSecret!, DateTime.UtcNow));
        Assert.False(MiniAppSecretPolicy.Verify(policy, app!, created.ClientSecret!, app!.PreviousClientSecretExpiresAt!.Value));
        Assert.DoesNotContain(created.ClientSecret!, app.PreviousClientSecretHash!);
        Assert.DoesNotContain(next.ClientSecret!, app.ClientSecretHash);
        await f.Management.RevokePreviousSecretAsync(f.Owner.Id, created.Id, default);
        Assert.False(MiniAppSecretPolicy.Verify(policy, app, created.ClientSecret!, DateTime.UtcNow));
        Assert.True(MiniAppSecretPolicy.Verify(policy, app, next.ClientSecret!, DateTime.UtcNow));
    }

    [Fact]
    public async Task ImmediateRotationAndInvalidGraceNeverAcceptOldSecret()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.SsoInput(), default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RotateSecretAsync(f.Owner.Id, created.Id, 1441, default));
        await f.Management.RotateSecretAsync(f.Owner.Id, created.Id, default);
        var app = await f.Db.MiniApps.FindAsync(created.Id);
        Assert.False(MiniAppSecretPolicy.Verify(new ClientCredentialService(), app!, created.ClientSecret!, DateTime.UtcNow));
        var metadata = await f.Management.GetCredentialsAsync(f.Owner.Id, created.Id, default);
        Assert.True(metadata.CanRotate); Assert.Null(metadata.PreviousSecretExpiresAt);
    }

    [Fact]
    public async Task DomainChallengesHaveRandomRootPathsExpireAndRenewWithoutDestroyingOtherEvidence()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var domain = await f.Management.AddDomainAsync(f.Owner.Id, created.Id, new("example.com"), default);
        Assert.Matches("^ankt-verify-[a-f0-9]{32}\\.txt$", domain.VerificationFileName!);
        Assert.Equal($"https://example.com/{domain.VerificationFileName}", domain.VerificationUrl);
        Assert.Equal("ankt-domain-verification=" + domain.ChallengeToken, domain.FileContent);
        var stored = await f.Db.MiniAppVerifiedDomains.FindAsync(domain.Id);
        Assert.NotEqual(domain.ChallengeToken, stored!.ChallengeToken);
        Assert.DoesNotContain(domain.ChallengeToken, stored.ProtectedChallengeToken!);
        stored.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.VerifyDomainAsync(f.Owner.Id, created.Id, domain.Id, default));
        Assert.Equal("DOMAIN_CHALLENGE_EXPIRED", error.Code);
        var other = await f.Management.AddDomainAsync(f.Owner.Id, created.Id, new("other.example.com"), default);
        var otherEntity = await f.Db.MiniAppVerifiedDomains.FindAsync(other.Id);
        otherEntity!.VerifiedAt = DateTime.UtcNow; await f.Db.SaveChangesAsync();
        var renewed = await f.Management.RenewDomainAsync(f.Owner.Id, created.Id, domain.Id, default);
        Assert.NotEqual(domain.ChallengeToken, renewed.ChallengeToken);
        Assert.NotEqual(domain.VerificationFileName, renewed.VerificationFileName);
        Assert.NotNull(otherEntity.VerifiedAt);
    }

    [Fact]
    public async Task LegacyVerifiedDomainsRetainOriginalRootFileAndContent()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        await f.Db.MiniAppVerifiedDomains.Where(d => d.MiniAppId == created.Id).ExecuteDeleteAsync();
        f.Db.MiniAppVerifiedDomains.Add(new() { MiniAppId = created.Id, Host = "example.com", ChallengeToken = "legacy-token", VerifiedAt = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        var domains = await f.Management.GetDomainsAsync(f.Owner.Id, created.Id, default);
        Assert.Equal("ankt-mini-app-verification.txt", domains[0].VerificationFileName);
        Assert.Equal("legacy-token", domains[0].FileContent); Assert.NotNull(domains[0].VerifiedAt);
    }
}
