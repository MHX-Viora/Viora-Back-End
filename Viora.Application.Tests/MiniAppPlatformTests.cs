using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class MiniAppPlatformTests
{
    private static MiniAppConfigurationInput Input(Fixture f) => f.Input() with { SupportEmail = "support@example.com", PrivacyPolicyUrl = "https://example.com/privacy" };
    private static MiniAppManagementService Service(Fixture f, bool connectivity = true) => new(f.Db, new ClientCredentialService(), new DomainProbe(), new TechnicalProbe(connectivity));
    private sealed class DomainProbe : IMiniAppDomainVerifier { public Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken ct) => Task.FromResult(true); }
    private sealed class TechnicalProbe(bool successful) : IMiniAppTechnicalProbe
    {
        public Task<MiniAppTechnicalCheckDto> CheckAsync(string code, string url, CancellationToken ct) => Task.FromResult(new MiniAppTechnicalCheckDto(code, successful, successful ? "Passed fixture" : "Unavailable fixture", successful ? 200 : 503, 1));
    }

    [Fact]
    public async Task SubmissionNeedsMatchingSuccessfulTestAndApprovalDoesNotPublish()
    {
        await using var f = await Fixture.Create(true); var service = Service(f);
        var app = await service.CreateAsync(f.Owner.Id, Input(f), default); await f.Verify(app.Id);
        Assert.Equal("TECHNICAL_TEST_REQUIRED", (await Assert.ThrowsAsync<MiniAppException>(() => service.SubmitReviewAsync(f.Owner.Id, app.Id, default))).Code);
        var test = await service.RunTechnicalTestAsync(f.Owner.Id, app.Id, default); Assert.True(test.Passed); Assert.Equal("Limited", test.Mode);
        await service.SubmitReviewAsync(f.Owner.Id, app.Id, default);
        await service.DecideVersionAsync(f.Other.Id, app.Id, new(1, "UnderReview"), default);
        await service.DecideVersionAsync(f.Other.Id, app.Id, new(1, "Approved"), default);
        Assert.Empty((await f.Runtime.GetActiveAsync(default)).Items);
        Assert.Equal(0, (await service.GetAppAsync(app.Id, default))!.PublishedVersion);
        await service.PublishOwnedAsync(f.Owner.Id, app.Id, 1, default);
        Assert.Single((await f.Runtime.GetActiveAsync(default)).Items);
        await service.UnpublishOwnedAsync(f.Owner.Id, app.Id, default);
        Assert.Empty((await f.Runtime.GetActiveAsync(default)).Items);
        await service.PublishOwnedAsync(f.Owner.Id, app.Id, 1, default);
        Assert.Single((await f.Runtime.GetActiveAsync(default)).Items);
        Assert.Contains(await f.Db.Notifications.ToListAsync(), n => n.Title == "Mini App đã xuất bản");
    }

    [Fact]
    public async Task FailedProbeCannotSubmitAndDraftChangesInvalidateSuccessfulTest()
    {
        await using var f = await Fixture.Create(true); var service = Service(f, false);
        var app = await service.CreateAsync(f.Owner.Id, Input(f), default); await f.Verify(app.Id);
        Assert.False((await service.RunTechnicalTestAsync(f.Owner.Id, app.Id, default)).Passed);
        await Assert.ThrowsAsync<MiniAppException>(() => service.SubmitReviewAsync(f.Owner.Id, app.Id, default));
        await f.Db.MiniAppTechnicalTests.ExecuteUpdateAsync(update => update.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-1)));
        service = Service(f); Assert.True((await service.RunTechnicalTestAsync(f.Owner.Id, app.Id, default)).Passed);
        await service.UpdateOwnedAsync(f.Owner.Id, app.Id, Input(f) with { Name = "Changed after check" }, default);
        Assert.Equal("TECHNICAL_TEST_REQUIRED", (await Assert.ThrowsAsync<MiniAppException>(() => service.SubmitReviewAsync(f.Owner.Id, app.Id, default))).Code);
        await Assert.ThrowsAsync<MiniAppException>(() => service.RunTechnicalTestAsync(f.Owner.Id, app.Id, default));
    }

    [Fact]
    public async Task SubmittedSnapshotCannotBeEditedAndChangesRequestedNeedsReason()
    {
        await using var f = await Fixture.Create(true); var service = Service(f);
        var app = await service.CreateAsync(f.Owner.Id, Input(f), default); await f.Verify(app.Id);
        await service.RunTechnicalTestAsync(f.Owner.Id, app.Id, default); await service.SubmitReviewAsync(f.Owner.Id, app.Id, default);
        await Assert.ThrowsAsync<MiniAppException>(() => service.UpdateOwnedAsync(f.Owner.Id, app.Id, Input(f) with { Permissions = ["profile.email"] }, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.DecideVersionAsync(f.Other.Id, app.Id, new(1, "ChangesRequested"), default));
        await service.DecideVersionAsync(f.Other.Id, app.Id, new(1, "ChangesRequested", "Add support contact"), default);
        Assert.Equal("ChangesRequested", (await service.GetVersionsAsync(app.Id, default)).Single().ReviewState);
        await service.UpdateOwnedAsync(f.Owner.Id, app.Id, Input(f) with { Name = "Corrected" }, default);
        Assert.Equal("Example app", (await service.GetVersionsAsync(app.Id, default)).Single().Configuration.Name);
        await Assert.ThrowsAsync<MiniAppException>(() => service.PublishOwnedAsync(f.Owner.Id, app.Id, 1, default));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Analyst")]
    public async Task ReadOnlyRolesCannotWriteAndCannotEscalate(string role)
    {
        await using var f = await Fixture.Create(true); var service = Service(f);
        var app = await service.CreateAsync(f.Owner.Id, Input(f), default);
        var member = await service.AddTeamAsync(f.Owner.Id, new(f.Other.Id, role), default);
        Assert.NotNull(await service.GetOwnedAsync(f.Other.Id, app.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.UpdateOwnedAsync(f.Other.Id, app.Id, Input(f), default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.CreateAsync(f.Other.Id, Input(f) with { Slug = "another" }, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.UpdateTeamRoleAsync(f.Other.Id, member.Id, new("Owner"), default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.AddDomainAsync(f.Other.Id, app.Id, new("another.example.com"), default));
        if (role == "Viewer") await Assert.ThrowsAsync<MiniAppException>(() => service.GetAnalyticsAsync(f.Other.Id, app.Id, default));
        else Assert.NotNull(await service.GetAnalyticsAsync(f.Other.Id, app.Id, default));
    }

    [Fact]
    public async Task LockedAccountCannotUseDeveloperApisAndAdminCannotGrantOwner()
    {
        await using var f = await Fixture.Create(true); var service = Service(f);
        var app = await service.CreateAsync(f.Owner.Id, Input(f), default);
        await Assert.ThrowsAsync<MiniAppException>(() => service.AddTeamAsync(f.Owner.Id, new(f.Other.Id, "Owner"), default));
        f.Owner.Status = AccountStatus.Banned; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<MiniAppException>(() => service.GetOwnedAsync(f.Owner.Id, app.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.CreateAsync(f.Owner.Id, Input(f), default));
    }

    [Fact]
    public async Task ReauthProofIsBoundToAccountTargetOperationAndSingleUse()
    {
        await using var f = await Fixture.Create(true);
        var passwords = new AspNetIdentityPasswordHasher(); f.Owner.PasswordHash = passwords.Hash("fixture-password"); await f.Db.SaveChangesAsync();
        var service = new DeveloperReauthenticationService(f.Db, passwords);
        var app = await f.Management.CreateAsync(f.Owner.Id, Input(f), default);
        var proof = await service.VerifyAsync(f.Owner.Id, new("fixture-password", "RotateSecret", app.Id), default);
        await Assert.ThrowsAsync<MiniAppException>(() => service.ConsumeAsync(f.Other.Id, "RotateSecret", app.Id, proof.Proof, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.ConsumeAsync(f.Owner.Id, "ChangeDomain", app.Id, proof.Proof, default));
        await Assert.ThrowsAsync<MiniAppException>(() => service.ConsumeAsync(f.Owner.Id, "RotateSecret", Guid.NewGuid(), proof.Proof, default));
        await service.ConsumeAsync(f.Owner.Id, "RotateSecret", app.Id, proof.Proof, default);
        await Assert.ThrowsAsync<MiniAppException>(() => service.ConsumeAsync(f.Owner.Id, "RotateSecret", app.Id, proof.Proof, default));
        Assert.DoesNotContain(await f.Db.MiniAppAuditLogs.ToListAsync(), l => (l.Detail ?? "").Contains(proof.Proof) || (l.Detail ?? "").Contains("fixture-password"));
    }

    [Fact]
    public async Task ExpiredProofAndPasswordlessAccountFailClosed()
    {
        await using var f = await Fixture.Create(true); var service = new DeveloperReauthenticationService(f.Db, new AspNetIdentityPasswordHasher());
        Assert.Equal("REAUTH_UNAVAILABLE", (await Assert.ThrowsAsync<MiniAppException>(() => service.VerifyAsync(f.Owner.Id, new("anything", "ManageTeam"), default))).Code);
        f.Owner.PasswordHash = new AspNetIdentityPasswordHasher().Hash("fixture-password"); await f.Db.SaveChangesAsync();
        var proof = await service.VerifyAsync(f.Owner.Id, new("fixture-password", "ManageTeam"), default);
        await f.Db.DeveloperReauths.ExecuteUpdateAsync(update => update.SetProperty(p => p.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        await Assert.ThrowsAsync<MiniAppException>(() => service.ConsumeAsync(f.Owner.Id, "ManageTeam", null, proof.Proof, default));
    }

    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com:8443/")]
    public async Task TechnicalProbeRejectsUnsafeTargetsWithoutNetwork(string url)
    {
        Assert.False((await new MiniAppTechnicalProbe().CheckAsync("URL", url, default)).Passed);
    }
}
