using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Xunit;

public sealed class MiniAppPublicationPersistenceTests
{
    [Fact]
    public async Task PublicationMustReferenceTheReviewsExactVersionAndOneActivePublication()
    {
        await using var f = await SandboxFixture.Create();
        var v1 = MiniAppVersionPersistenceTests.Version(f.App);
        v1.State = MiniAppVersionState.Submitted; v1.SubmittedAt = DateTime.UtcNow;
        var v2 = MiniAppVersionPersistenceTests.Version(f.App, 2);
        var admin = new Account { Email = "publication-admin@example.test" };
        var review = MiniAppReviewPersistenceTests.Review(f.App, v1.Id);
        review.Status = MiniAppReviewStatus.Approved; review.AssignedAdminAccountId = admin.Id;
        review.ReviewStartedAt = review.DecidedAt = DateTime.UtcNow;
        f.Db.AddRange(v1, v2, admin, review); await f.Db.SaveChangesAsync();
        var wrong = Publication(f.App, review); wrong.MiniAppVersionId = v2.Id;
        f.Db.Add(wrong); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        f.Db.Add(Publication(f.App, review)); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Db.Add(Publication(f.App, review)); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("status")]
    [InlineData("closure")]
    public async Task InvalidPublicationStateIsRejected(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        var version = MiniAppVersionPersistenceTests.Version(f.App);
        var review = MiniAppReviewPersistenceTests.Review(f.App, version.Id);
        f.Db.AddRange(version, review); await f.Db.SaveChangesAsync();
        var publication = Publication(f.App, review);
        switch (invalid)
        {
            case "environment": publication.Environment = MiniAppOAuthEnvironment.Sandbox; break;
            case "status": publication.Status = (MiniAppPublicationStatus)99; break;
            case "closure": publication.Status = MiniAppPublicationStatus.Archived; break;
        }
        f.Db.Add(publication); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task OperationalSuspensionRequiresAnAdminAndReasonAndNeverCreatesPublication()
    {
        await using var f = await SandboxFixture.Create();
        f.App.Status = MiniAppStatus.Published; await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.Set<MiniAppPublication>().ToListAsync());
        var state = new MiniAppOperationalState { MiniAppId = f.App.Id, Status = MiniAppOperationalStatus.Suspended };
        f.Db.Add(state); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear(); state.Status = MiniAppOperationalStatus.Active;
        f.Db.Add(state); await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.Set<MiniAppPublication>().ToListAsync());
    }

    internal static MiniAppPublication Publication(MiniApp app, MiniAppReviewRequest review) => new()
    {
        MiniAppId = app.Id, MiniAppVersionId = review.MiniAppVersionId, ApprovedReviewRequestId = review.Id,
        PublishedByDeveloperId = app.DeveloperAccountId, PublishedAt = DateTime.UtcNow
    };
}
