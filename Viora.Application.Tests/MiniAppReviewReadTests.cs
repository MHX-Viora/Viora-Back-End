using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewReadTests
{
    [Fact]
    public async Task OwnedDetailKeepsSealedMetadataAndEvidenceAfterDraftChangesAndEvidenceExpiry()
    {
        await using var f = await SandboxFixture.Create(); var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        await f.Db.MiniApps.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "New draft name")); f.Clock.Now += TimeSpan.FromMinutes(6);
        var service = new MiniAppReviewService(f.Db, new(f.Db, f.Clock));
        var detail = await service.GetOwnedAsync(f.Actor.Id, f.Source.Id, f.App.AppId, review.Id, default);
        Assert.Equal("Fixture", detail.Snapshot.Name); Assert.Equal(review, detail.Review);
        Assert.Equal(detail.SnapshotHash, detail.Evidence.SnapshotHash); Assert.Single(detail.Actions); Assert.Empty(detail.Changes);
        var page = await service.ListOwnedAsync(f.Actor.Id, f.Source.Id, f.App.AppId, 1, 20, default);
        Assert.Equal(1, page.Total); Assert.Equal(review, Assert.Single(page.Items));
        Assert.Equal(404, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.GetOwnedAsync(Guid.NewGuid(), f.Source.Id, f.App.AppId, review.Id, default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ListOwnedAsync(f.Actor.Id, f.Source.Id, f.App.AppId, 1, 101, default))).Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("evidence")]
    public async Task CorruptSealedRecordsNeverBecomeAValidOwnedDetail(string corrupt)
    {
        await using var f = await SandboxFixture.Create(); var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        if (corrupt == "hash") await f.Db.MiniAppVersions.ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, new string('0', 64)));
        else await f.Db.MiniAppReviewRequests.ExecuteUpdateAsync(s => s.SetProperty(x => x.EvidenceAttestationJson, "{}"));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => new MiniAppReviewService(f.Db, new(f.Db, f.Clock)).GetOwnedAsync(f.Actor.Id, f.Source.Id, f.App.AppId, review.Id, default));
    }
}
