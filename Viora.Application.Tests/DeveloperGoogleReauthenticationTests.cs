using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class DeveloperGoogleReauthenticationTests
{
    private sealed class Organization(Guid? id) : IDeveloperOrganizationContext { public Guid? OrganizationId { get; set; } = id; }
    private sealed class GoogleVerifier(GoogleVerifiedIdentity? identity) : IGoogleIdentityTokenVerifier
    {
        public Task<GoogleVerifiedIdentity?> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(token == "signed-fixture" ? identity : null);
    }

    [Fact]
    public async Task PasswordlessGoogleReauthBindsExistingSubjectAndRejectsProviderReplay()
    {
        await using var f = await Fixture.Create(true);
        f.Db.ExternalLogins.Add(new ExternalLogin { AccountId = f.Owner.Id, Provider = "google.com", ProviderSubject = "linked-subject" });
        await f.Db.SaveChangesAsync();
        var service = new DeveloperReauthenticationService(f.Db, new AspNetIdentityPasswordHasher(), new GoogleVerifier(new("linked-subject", "owner@example.com", AuthenticatedAt: DateTimeOffset.UtcNow)));
        var app = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var input = new DeveloperReauthInput(null, "RotateSecret", app.Id, "signed-fixture");
        var proof = await service.VerifyAsync(f.Owner.Id, input, default);
        await service.ConsumeAsync(f.Owner.Id, "RotateSecret", app.Id, proof.Proof, default);
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyAsync(f.Owner.Id, input, default))).Code);
        Assert.DoesNotContain(await f.Db.MiniAppAuditLogs.ToListAsync(), l => (l.Detail ?? "").Contains("signed-fixture"));
        Assert.Equal(64, (await f.Db.DeveloperReauths.SingleAsync()).ProviderAuthenticationHash!.Length);
    }

    [Theory]
    [InlineData("unlinked-subject", 0)]
    [InlineData("linked-subject", -6)]
    [InlineData("linked-subject", 1)]
    public async Task WrongSubjectStaleOrFutureGoogleAuthenticationFails(string subject, int ageMinutes)
    {
        await using var f = await Fixture.Create(true);
        f.Db.ExternalLogins.Add(new ExternalLogin { AccountId = f.Owner.Id, Provider = "google.com", ProviderSubject = "linked-subject" });
        await f.Db.SaveChangesAsync();
        var service = new DeveloperReauthenticationService(f.Db, new AspNetIdentityPasswordHasher(), new GoogleVerifier(new(subject, "owner@example.com", AuthenticatedAt: DateTimeOffset.UtcNow.AddMinutes(ageMinutes))));
        Assert.Equal("REAUTH_FAILED", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyAsync(f.Owner.Id, new(null, "ManageTeam", FirebaseToken: "signed-fixture"), default))).Code);
        Assert.Empty(await f.Db.DeveloperReauths.ToListAsync());
    }

    [Fact]
    public async Task InvalidGoogleTokenAndMixedCredentialsFailClosed()
    {
        await using var f = await Fixture.Create(true);
        var service = new DeveloperReauthenticationService(f.Db, new AspNetIdentityPasswordHasher(), new GoogleVerifier(null));
        Assert.Equal("REAUTH_FAILED", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyAsync(f.Owner.Id, new(null, "ManageTeam", FirebaseToken: "invalid"), default))).Code);
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyAsync(f.Owner.Id, new("password", "ManageTeam", FirebaseToken: "invalid"), default))).Code);
    }

    [Fact]
    public async Task OrganizationProofCannotBeConsumedInAnotherOrganization()
    {
        await using var f = await Fixture.Create(true);
        var passwords = new AspNetIdentityPasswordHasher(); f.Owner.PasswordHash = passwords.Hash("FixturePassword1"); f.Owner.EmailVerifiedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        var organization = new Organization((await f.Db.Developers.SingleAsync()).Id);
        var service = new DeveloperReauthenticationService(f.Db, passwords, organization: organization);
        var proof = await service.VerifyAsync(f.Owner.Id, new("FixturePassword1", "ManageTeam"), default);
        var initialOrganization = organization.OrganizationId;
        organization.OrganizationId = Guid.NewGuid();
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<MiniAppException>(() => service.ConsumeAsync(f.Owner.Id, "ManageTeam", null, proof.Proof, default))).Code);
        organization.OrganizationId = initialOrganization;
        await service.ConsumeAsync(f.Owner.Id, "ManageTeam", null, proof.Proof, default);
    }
}
