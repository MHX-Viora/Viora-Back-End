using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Xunit;

public sealed class MiniAppReviewPersistenceTests
{
    [Fact]
    public async Task OneActiveReviewAndSameAppVersionAreRequired()
    {
        await using var f = await SandboxFixture.Create();
        var version = MiniAppVersionPersistenceTests.Version(f.App);
        f.Db.Add(version); await f.Db.SaveChangesAsync();
        var review = Review(f.App, version.Id);
        f.Db.Add(review); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Db.Add(Review(f.App, version.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        var other = new MiniApp { DeveloperAccountId = f.App.DeveloperAccountId,
            AppId = "app_other_review", Name = "Other", Category = "other", CreateRequestId = Guid.NewGuid() };
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        f.Db.Add(Review(other, version.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("assignee")]
    [InlineData("decision")]
    [InlineData("status")]
    [InlineData("evidence")]
    public async Task InvalidReviewStateIsRejected(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        var version = MiniAppVersionPersistenceTests.Version(f.App); f.Db.Add(version);
        await f.Db.SaveChangesAsync(); var review = Review(f.App, version.Id);
        switch (invalid)
        {
            case "assignee": review.Status = MiniAppReviewStatus.InReview; break;
            case "decision": review.DecidedAt = DateTime.UtcNow; break;
            case "status": review.Status = (MiniAppReviewStatus)99; break;
            case "evidence": review.EvidenceAttestationJson = new string('x', 32769); break;
        }
        f.Db.Add(review); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ActionsRequireTypedActorAndUniqueActorRequestId()
    {
        await using var f = await SandboxFixture.Create();
        var action = Action(f.App); f.Db.Add(action); await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear(); var duplicate = Action(f.App); duplicate.RequestId = action.RequestId;
        f.Db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear(); var forged = Action(f.App); forged.ActorDeveloperId = null;
        f.Db.Add(forged); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear(); forged = Action(f.App); forged.ActorId = Guid.NewGuid();
        f.Db.Add(forged); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    internal static MiniAppReviewRequest Review(MiniApp app, Guid version) => new()
    {
        MiniAppId = app.Id, MiniAppVersionId = version, DeveloperAccountId = app.DeveloperAccountId,
        SubmittedAt = DateTime.UtcNow, EvidenceAttestationJson = "{}", CorrelationId = Guid.NewGuid()
    };

    internal static MiniAppReviewAction Action(MiniApp app) => new()
    {
        MiniAppId = app.Id, ActorId = app.DeveloperAccountId, ActorDeveloperId = app.DeveloperAccountId,
        RequestId = Guid.NewGuid(), PayloadHash = new string('a', 64), ResultJson = "{}",
        CorrelationId = Guid.NewGuid(), Action = MiniAppReviewActionKind.VersionCreated
    };
}
