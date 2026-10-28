using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionEligibilityTests
{
    [Fact]
    public async Task NonSsoPublicationNeedsNoOAuthClientAndUsesOnlyItsSealedTarget()
    {
        await using var f = await SandboxFixture.Create();
        var client = new MiniAppOAuthClient { MiniAppId = f.App.Id, ClientKey = "unused-production", ClientType = "Public", Environment = MiniAppOAuthEnvironment.Production };
        await MiniAppPublicationPolicyTests.Seal(f.Db, f.App, client,
            MiniAppVersionSnapshotTests.Snapshot() with { SsoEnabled = false, Sandbox = null }, f.Clock.Now.UtcDateTime);
        await f.Db.MiniAppPublications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductionOAuthClientId, (Guid?)null));
        await f.Db.MiniAppOAuthClients.Where(x => x.Id == client.Id).ExecuteDeleteAsync();
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Unreviewed draft")
            .SetProperty(x => x.WebsiteUrl, "https://different.example.com/new").SetProperty(x => x.Status, MiniAppStatus.Draft));
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        Assert.Equal("Example", target.Snapshot.Name);
        Assert.Equal("https://partner.example.com/app", target.Snapshot.WebsiteUrl);
        Assert.False(target.Snapshot.SsoEnabled);
        Assert.Null(target.Publication.ProductionOAuthClientId);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("legacy")]
    [InlineData("hash")]
    [InlineData("suspended")]
    [InlineData("unpublished")]
    [InlineData("rejected")]
    [InlineData("deleted")]
    [InlineData("developer")]
    [InlineData("locked")]
    [InlineData("domain")]
    [InlineData("expired-domain")]
    [InlineData("future-publication")]
    public async Task PublicEligibilityRechecksPublicationSafetyAndRejectsEveryBypass(string change)
    {
        await using var f = await SandboxFixture.Create();
        var client = await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var now = f.Clock.Now.UtcDateTime;
        switch (change)
        {
            case "legacy": await f.Db.MiniAppPublications.ExecuteDeleteAsync(); break;
            case "hash": await f.Db.MiniAppVersions.ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, new string('0', 64))); break;
            case "suspended": await f.Db.MiniAppOperationalStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppOperationalStatus.Suspended).SetProperty(x => x.Reason, "Fixture suspended app").SetProperty(x => x.SuspendedAt, now).SetProperty(x => x.ChangedByAdminAccountId, f.Db.Accounts.Select(a => (Guid?)a.Id).First())); break;
            case "unpublished": await f.Db.MiniAppPublications.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppPublicationStatus.UnpublishedByOwner).SetProperty(x => x.UnpublishedAt, now)); break;
            case "rejected": await f.Db.MiniAppReviewRequests.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppReviewStatus.Rejected).SetProperty(x => x.DecisionReason, "Fixture rejected target")); break;
            case "deleted": await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, now)); break;
            case "developer": await f.Db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DeveloperStatus.Suspended)); break;
            case "locked": await f.Db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntil, now.AddMinutes(5))); break;
            case "domain": await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.Status, MiniAppDomainStatus.Revoked)); break;
            case "expired-domain": await f.Db.MiniAppDomainVerifications.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, now)); break;
            case "future-publication": await f.Db.MiniAppPublications.ExecuteUpdateAsync(s => s.SetProperty(x => x.PublishedAt, now.AddMinutes(1))); break;

        }
        if (change == "valid")
            Assert.Equal(client.Id, (await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, now, default)).Publication.ProductionOAuthClientId);
        else
        {
            var error = await Assert.ThrowsAsync<DeveloperAuthException>(() => MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, now, default));
            Assert.Equal(404, error.Status); Assert.Equal("APP_NOT_FOUND", error.Code);
        }
    }
}
