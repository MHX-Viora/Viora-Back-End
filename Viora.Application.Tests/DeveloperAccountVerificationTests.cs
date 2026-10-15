using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Application.MiniApps;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class DeveloperAccountVerificationTests
{
    private sealed class Firebase(FirebaseVerifiedIdentity? identity) : IFirebaseIdentityTokenVerifier
    {
        public Task<FirebaseVerifiedIdentity?> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(token == "signed-fixture" ? identity : null);
    }
    [Fact]
    public async Task MatchingRecentServerVerifiedEmailMarksOnlyAuthenticatedAccount()
    {
        await using var f = await Fixture.Create(true);
        var service = new DeveloperAccountVerificationService(f.Db, new Firebase(new(f.Owner.Email, null, DateTimeOffset.UtcNow)));
        Assert.Null((await service.GetAsync(f.Owner.Id, default)).EmailVerifiedAt);
        var verified = await service.VerifyEmailAsync(f.Owner.Id, "signed-fixture", default);
        Assert.NotNull(verified.EmailVerifiedAt); Assert.Equal(f.Owner.Email, verified.Email);
        Assert.Null(f.Other.EmailVerifiedAt);
        Assert.DoesNotContain(await f.Db.MiniAppAuditLogs.ToListAsync(), log => (log.Detail ?? "").Contains("signed-fixture"));
    }
    [Theory]
    [InlineData("other@example.com", 0)]
    [InlineData("owner@example.com", -6)]
    [InlineData("owner@example.com", 1)]
    public async Task ClientEmailMismatchOrNonRecentVerificationCannotMarkAccount(string email, int minutes)
    {
        await using var f = await Fixture.Create();
        var service = new DeveloperAccountVerificationService(f.Db, new Firebase(new(email, null, DateTimeOffset.UtcNow.AddMinutes(minutes))));
        Assert.Equal("EMAIL_VERIFICATION_FAILED", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyEmailAsync(f.Owner.Id, "signed-fixture", default))).Code);
        Assert.Null(f.Owner.EmailVerifiedAt);
    }
    [Fact]
    public async Task InvalidTokenOrPhoneOnlyIdentityCannotVerifyEmail()
    {
        await using var f = await Fixture.Create();
        var service = new DeveloperAccountVerificationService(f.Db, new Firebase(new(null, "+84901234567", DateTimeOffset.UtcNow)));
        Assert.Equal("EMAIL_VERIFICATION_FAILED", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyEmailAsync(f.Owner.Id, "invalid", default))).Code);
        Assert.Equal("EMAIL_VERIFICATION_FAILED", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyEmailAsync(f.Owner.Id, "signed-fixture", default))).Code);
    }
    [Fact]
    public async Task PasswordSensitiveProofRequiresVerifiedEmailAndSucceedsAfterVerification()
    {
        await using var f = await Fixture.Create(true); var passwords = new AspNetIdentityPasswordHasher();
        f.Owner.PasswordHash = passwords.Hash("SafeFixture1"); await f.Db.SaveChangesAsync();
        var reauth = new DeveloperReauthenticationService(f.Db, passwords);
        Assert.Equal("EMAIL_VERIFICATION_REQUIRED", (await Assert.ThrowsAsync<MiniAppException>(() => reauth.VerifyAsync(f.Owner.Id, new("SafeFixture1", "ManageTeam"), default))).Code);
        Assert.Empty(await f.Db.DeveloperReauths.ToListAsync());
        await new DeveloperAccountVerificationService(f.Db, new Firebase(new(f.Owner.Email, null, DateTimeOffset.UtcNow))).VerifyEmailAsync(f.Owner.Id, "signed-fixture", default);
        Assert.NotNull(await reauth.VerifyAsync(f.Owner.Id, new("SafeFixture1", "ManageTeam"), default));
    }
}
