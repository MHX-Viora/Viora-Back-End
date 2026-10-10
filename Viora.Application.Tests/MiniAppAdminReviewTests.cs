using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;

public sealed class MiniAppAdminReviewTests
{
    [Fact]
    public async Task DecisionsBindExactSubmittedVersionAndCannotApproveDraft()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.ReviewAppVersionAsync(f.Other.Id, created.Id, 1, true, null, default));
        await f.Verify(created.Id); await f.Management.SubmitReviewAsync(f.Owner.Id, created.Id, default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.ReviewAppVersionAsync(f.Other.Id, created.Id, 2, true, null, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.ReviewAppVersionAsync(f.Other.Id, created.Id, null, false, "Wrong", default));
        Assert.Equal(0, (await f.Management.GetAppAsync(created.Id, default))!.PublishedVersion);
        await f.Management.ReviewAppVersionAsync(f.Other.Id, created.Id, 1, true, null, default);
        Assert.Equal(1, (await f.Management.GetAppAsync(created.Id, default))!.PublishedVersion);
    }
    [Fact]
    public async Task ReviewContextUsesActualOwnerEvenWhenDeveloperNamesMatch()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        f.Db.Developers.Add(new Developer { Name = "Developer", Email = "different@example.com", AccountId = f.Other.Id });
        await f.Db.SaveChangesAsync();
        var context = await f.Management.GetReviewContextAsync(created.Id, default);
        Assert.Equal(f.Owner.Id, context.Developer.AccountId);
        Assert.Equal("dev@example.com", context.Developer.Email);
    }
    [Fact]
    public async Task FailedAdminDomainRecheckInvalidatesProofAndPreventsPublication()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        await f.Verify(created.Id); await f.Management.SubmitReviewAsync(f.Owner.Id, created.Id, default);
        var domain = await f.Db.MiniAppVerifiedDomains.SingleAsync(d => d.MiniAppId == created.Id);
        await f.Db.MiniAppVerifiedDomains.ExecuteUpdateAsync(s => s.SetProperty(d => d.LastAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
        var rejecting = new MiniAppManagementService(f.Db, new ClientCredentialService(), new RejectingVerifier());
        await Assert.ThrowsAsync<MiniAppException>(() => rejecting.VerifyAdminDomainAsync(f.Other.Id, created.Id, domain.Id, default));
        Assert.Null((await rejecting.GetReviewContextAsync(created.Id, default)).Domains.Single().VerifiedAt);
        Assert.Contains(await f.Db.MiniAppAuditLogs.ToListAsync(), a => a.Action == "DomainVerificationFailed" && a.ActorAccountId == f.Other.Id);
        var error = await Assert.ThrowsAsync<MiniAppException>(() => rejecting.ReviewAppVersionAsync(f.Other.Id, created.Id, 1, true, null, default));
        Assert.Equal("DOMAIN_NOT_VERIFIED", error.Code);
        await rejecting.ReviewAppVersionAsync(f.Other.Id, created.Id, 1, false, "Domain no longer verified", default);
        Assert.Equal("Rejected", (await rejecting.GetAppAsync(created.Id, default))!.Status);
    }
    [Fact]
    public async Task SuspensionRestorationDoesNotApproveWaitingUpdate()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Publish(f.Input());
        await f.Management.UpdateOwnedAsync(f.Owner.Id, created.Id, f.Input() with { Name = "Update" }, default);
        await f.Management.SubmitReviewAsync(f.Owner.Id, created.Id, default);
        await f.Management.SetAppStatusAsync(f.Other.Id, created.Id, MiniAppStatus.Suspended, "Review later", default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.ReviewAppVersionAsync(f.Other.Id, created.Id, 2, true, null, default));
        await f.Management.ReactivateAppAsync(f.Other.Id, created.Id, default);
        var app = (await f.Management.GetAppAsync(created.Id, default))!;
        Assert.Equal(1, app.PublishedVersion); Assert.Equal(2, app.PendingVersion);
        await f.Management.ReviewAppVersionAsync(f.Other.Id, created.Id, 2, true, null, default);
        Assert.Equal(2, (await f.Management.GetAppAsync(created.Id, default))!.PublishedVersion);
    }
    private sealed class RejectingVerifier : IMiniAppDomainVerifier
    {
        public Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    [Fact]
    public async Task LegacyPendingWithoutSubmittedSnapshotCannotPublishUnverifiedDraft()
    {
        await using var f = await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var app = await f.Db.MiniApps.SingleAsync(a => a.Id == created.Id);
        app.Status = MiniAppStatus.PendingReview;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Active, null, default));
        Assert.Equal("REVIEW_VERSION_REQUIRED", error.Code);
        Assert.Equal(0, app.PublishedVersion);
        Assert.Equal(MiniAppStatus.PendingReview, app.Status);
        await f.Management.UpdateAppAsync(f.Other.Id, app.Id, f.Input(), default);
        Assert.Equal(MiniAppStatus.Draft, app.Status);
        Assert.Equal(0, app.PublishedVersion);
        await f.Verify(app.Id); await f.Management.SubmitReviewAsync(f.Owner.Id, app.Id, default);
        await f.Management.ReviewAppVersionAsync(f.Other.Id, app.Id, 1, true, null, default);
        Assert.Equal(1, app.PublishedVersion);
    }
}
