using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionCatalogPersistenceTests
{
    [Theory]
    [InlineData("key")]
    [InlineData("type")]
    [InlineData("scopes")]
    public async Task BridgeProjectionRejectsLegacyClientConfiguration(string missing)
    {
        await using var f = await SandboxFixture.Create();
        var client = new MiniAppOAuthClient { MiniAppId = f.App.Id, ClientKey = "bridge-projection-unused", ClientType = "Public", Environment = MiniAppOAuthEnvironment.Production };
        await MiniAppPublicationPolicyTests.Seal(f.Db, f.App, client,
            MiniAppVersionSnapshotTests.Snapshot() with { SsoEnabled = false, Sandbox = null, Production = null }, f.Clock.Now.UtcDateTime);
        var row = Projection(await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default));
        if (missing == "key") row.ProductionClientKey = "unexpected";
        if (missing == "type") row.ProductionClientType = "Invalid";
        if (missing == "scopes") row.ProductionScopes = "unexpected";
        f.Db.Add(row); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ProjectionHasOneExactPublicationTargetAndCannotBeEdited()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        var row = Projection(target); f.Db.Add(row); await f.Db.SaveChangesAsync();
        row.Name = "Unreviewed overwrite";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        f.Db.Add(Projection(target));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ProjectionCannotAssociatePublicationWithAnotherVersion()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        var other = MiniAppVersionPersistenceTests.Version(f.App, 2); f.Db.Add(other); await f.Db.SaveChangesAsync();
        var row = Projection(target); row.MiniAppVersionId = other.Id; f.Db.Add(row);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("hash")]
    [InlineData("description")]
    public async Task ProjectionRejectsUnboundedDisplayFields(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        var row = Projection(target);
        if (invalid == "name") row.Name = "";
        if (invalid == "hash") row.SnapshotHash = "invalid";
        if (invalid == "description") row.Description = new string('x', 301);
        f.Db.Add(row); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    internal static MiniAppCatalogPublication Projection(MiniAppProductionTarget target) => new()
    {
        PublicationId = target.Publication.Id, MiniAppId = target.Publication.MiniAppId,
        MiniAppVersionId = target.Version.Id, ApprovedReviewRequestId = target.Publication.ApprovedReviewRequestId,
        SnapshotHash = target.Version.SnapshotHash, Name = target.Snapshot.Name, Category = target.Snapshot.Category,
        LogoUrl = target.Snapshot.LogoUrl, Description = target.Snapshot.ShortDescription,
        WebsiteUrl = target.Snapshot.WebsiteUrl!, Domain = target.Snapshot.Domain!, SsoEnabled = target.Snapshot.SsoEnabled,
        ProductionClientKey = target.Snapshot.SsoEnabled ? target.Snapshot.Production!.ClientId : null,
        ProductionClientType = target.Snapshot.SsoEnabled ? target.Snapshot.Production!.ClientType : null,
        ProductionScopes = target.Snapshot.SsoEnabled ? string.Join(' ', target.Snapshot.Production!.Scopes) : null,
        VersionName = target.Version.VersionName, PublishedAt = target.Publication.PublishedAt
    };
}
