using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionCatalogTests
{
    [Fact]
    public async Task DisabledBridgeDoesNotAdvertiseMiniApps()
    {
        await using var f = await SandboxFixture.Create();
        await AddUnitPublication(f, 's', true);
        var plain = await AddUnitPublication(f, 'n');
        var service = new MiniAppPublicCatalogService(f.Db, f.Clock, new() { Enabled = true }, bridgeEnabled: false);
        Assert.Empty((await service.ListAsync(new(), default)).Items);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.DetailAsync("app_" + new string('s', 43), default));
    }
    [Fact]
    public async Task SafetyFiltersRunBeforeCountAndPaginationAndDisplayOnlyReviewedMetadata()
    {
        await using var f = await SandboxFixture.Create();
        var first = await AddUnitPublication(f, 'a');
        var expired = await AddUnitPublication(f, 'b');
        var newest = await AddUnitPublication(f, 'c');
        await f.Db.MiniAppDomainVerifications.Where(x => x.MiniAppId == expired.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now.UtcDateTime));
        var service = Service(f);
        var page = await service.ListAsync(new(1, 1), default);
        Assert.Equal(2, page.Total); Assert.Equal(newest.AppId, Assert.Single(page.Items).Id);
        Assert.Equal("Published c", page.Items[0].Name);
        Assert.Equal(first.AppId, Assert.Single((await service.ListAsync(new(2, 1), default)).Items).Id);
        Assert.Empty((await service.ListAsync(new(3, 1), default)).Items);
        var detail = await service.DetailAsync(newest.AppId, default);
        Assert.True(detail.CanOpen); Assert.True(detail.LoginAvailable);
        Assert.Equal(["platform.getContext", "platform.close"], detail.Capabilities);
    }

    [Theory]
    [InlineData("client-key")]
    [InlineData("client-type")]
    [InlineData("client-scopes")]
    [InlineData("suspend")]
    [InlineData("developer")]
    [InlineData("unpublish")]
    public async Task CatalogAndDetailNeverAuthorizeAStaleProductionTarget(string change)
    {
        await using var f = await SandboxFixture.Create();
        var app = await AddUnitPublication(f, 's', true);
        Assert.Single((await Service(f).ListAsync(new(), default)).Items);
        switch (change)
        {
            case "client-key": await f.Db.MiniAppOAuthClients.Where(x => x.MiniAppId == app.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientKey, "replacement")); break;
            case "client-type": await f.Db.MiniAppOAuthClients.Where(x => x.MiniAppId == app.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientType, "Public")); break;
            case "client-scopes": await f.Db.MiniAppOAuthClients.Where(x => x.MiniAppId == app.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AllowedScopes, "openid profile")); break;
            case "developer": await f.Db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntil, f.Clock.Now.UtcDateTime.AddMinutes(1))); break;
            case "unpublish": await f.Db.MiniAppPublications.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppPublicationStatus.UnpublishedByOwner).SetProperty(x => x.UnpublishedAt, f.Clock.Now.UtcDateTime)); break;
            case "suspend":
                await f.Db.MiniAppOperationalStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppOperationalStatus.Suspended)
                    .SetProperty(x => x.Reason, "Unit suspension reason").SetProperty(x => x.SuspendedAt, f.Clock.Now.UtcDateTime)
                    .SetProperty(x => x.ChangedByAdminAccountId, f.Db.Accounts.Select(x => (Guid?)x.Id).First())); break;
        }
        if (change is "client-key" or "client-type" or "client-scopes")
        { Assert.Equal(1, (await Service(f).ListAsync(new(), default)).Total); return; }
        Assert.Equal(0, (await Service(f).ListAsync(new(), default)).Total);
        var error = await Assert.ThrowsAsync<DeveloperAuthException>(() => Service(f).DetailAsync(app.AppId, default));
        Assert.Equal(404, error.Status); Assert.Equal("APP_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task ModuleDefaultsOffAndLegacyPublishedAloneIsNeverDiscoverable()
    {
        await using var f = await SandboxFixture.Create();
        f.App.Status = MiniAppStatus.Published; await f.Db.SaveChangesAsync();
        Assert.Empty((await Service(f).ListAsync(new(), default)).Items);
        var service = new MiniAppPublicCatalogService(f.Db, f.Clock, new());
        Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ListAsync(new(), default))).Status);
    }

    internal static MiniAppPublicCatalogService Service(SandboxFixture f) => new(f.Db, f.Clock, new() { Enabled = true }, bridgeEnabled: true);

    // Unit query data only. Business E2E must create these states via actual review/publish workflows.
    internal static async Task<MiniApp> AddUnitPublication(SandboxFixture f, char suffix, bool sso = false)
    {
        f.Clock.Now += TimeSpan.FromSeconds(1); var now = f.Clock.Now.UtcDateTime;
        var app = new MiniApp { AppId = "app_" + new string(suffix, 43), DeveloperAccountId = f.Actor.Id, CreateRequestId = Guid.NewGuid(),
            Name = "Mutable draft", Category = "utilities", WebsiteUrl = "https://partner.example.com/app" };
        var client = sso ? new MiniAppOAuthClient { MiniAppId = app.Id, ClientKey = "catalog-" + suffix,
            Environment = MiniAppOAuthEnvironment.Production, ClientType = "Confidential", AllowedScopes = "openid" } : null;
        var input = MiniAppVersionSnapshotTests.Snapshot() with { Name = "Published " + suffix, SsoEnabled = sso,
            Sandbox = null, Production = client == null ? null : new(client.Id, client.ClientKey, client.ClientType,
                ["openid"], ["https://partner.example.com/production"], false) };
        var canonical = MiniAppVersionSnapshotPolicy.Create(input);
        var version = MiniAppVersionPersistenceTests.Version(app); version.State = MiniAppVersionState.Submitted;
        version.SubmittedAt = now; version.SnapshotJson = canonical.Json; version.SnapshotHash = canonical.Hash;
        var admin = await f.Db.Accounts.FirstOrDefaultAsync();
        if (admin == null) { admin = new() { Email = "catalog-unit-admin@example.test", Role = AccountRole.Admin }; f.Db.Add(admin); }
        var review = MiniAppReviewPersistenceTests.Review(app, version.Id); review.SubmittedAt = now;
        review.Status = MiniAppReviewStatus.Approved; review.AssignedAdminAccountId = admin.Id; review.ReviewStartedAt = review.DecidedAt = now;
        var publication = MiniAppPublicationPersistenceTests.Publication(app, review); publication.PublishedAt = now; publication.ProductionOAuthClientId = client?.Id;
        f.Db.AddRange(app, version, review, publication, new MiniAppOperationalState { MiniAppId = app.Id },
            new MiniAppDomainVerification { MiniAppId = app.Id, Hostname = "partner.example.com", VerificationFileName = "unit-" + suffix + ".txt",
                Status = MiniAppDomainStatus.Verified, ExpiresAt = now.AddHours(1) });
        if (client != null) f.Db.Add(client);
        await f.Db.SaveChangesAsync();
        f.Db.Add(MiniAppProductionCatalogPersistenceTests.Projection(new(publication, version, canonical.Snapshot)));
        await f.Db.SaveChangesAsync(); return app;
    }
}
