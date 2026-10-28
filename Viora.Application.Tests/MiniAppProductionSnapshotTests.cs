using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppProductionSnapshotTests
{
    [Fact]
    public async Task BridgeProductionTargetUsesApprovedSnapshotAfterDraftChanges()
    {
        await using var f = await SandboxFixture.Create();
        await MiniAppPublicationPolicyTests.ApprovedPublication(f);
        f.App.Name = "Unreviewed name";
        f.App.WebsiteUrl = "https://other.example.com/draft";
        await f.Db.SaveChangesAsync();
        var effective = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        Assert.Equal("Example", effective.Snapshot.Name);
        Assert.Equal("https://partner.example.com/app", effective.Snapshot.WebsiteUrl);
        Assert.Equal("Unreviewed name", (await f.Db.MiniApps.AsNoTracking().SingleAsync()).Name);
    }
}
