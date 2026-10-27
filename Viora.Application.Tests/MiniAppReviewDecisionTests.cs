using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewDecisionTests
{
    internal static async Task<(MiniAppReviewSummaryDto Review, Guid Admin, Guid Session)> Started(SandboxFixture f)
    {
        var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        var actor = await MiniAppReviewAdminReadTests.Admin(f);
        return (await MiniAppReviewAssignmentTests.Service(f).StartAsync(actor.Admin, actor.Session, review.Id, new(Guid.NewGuid(), review.ConcurrencyToken), default), actor.Admin, actor.Session);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssigneeDecidesOnceWithReasonAndStructuredChangesAndOwnerMustCreateANewVersion(bool changes)
    {
        await using var f = await SandboxFixture.Create(); var current = await Started(f); var service = MiniAppReviewAssignmentTests.Service(f);
        var command = new MiniAppReviewDecisionRequest(Guid.NewGuid(), current.Review.ConcurrencyToken, "The reviewed page needs a correction",
            changes ? [new("websiteUrl", "Please correct the public application URL.")] : null);
        var result = changes ? await service.RequestChangesAsync(current.Admin, current.Session, current.Review.Id, command, default)
            : await service.RejectAsync(current.Admin, current.Session, current.Review.Id, command, default);
        Assert.Equal(changes ? "ChangesRequested" : "Rejected", result.Status);
        var replay = changes ? await service.RequestChangesAsync(current.Admin, current.Session, current.Review.Id, command, default)
            : await service.RejectAsync(current.Admin, current.Session, current.Review.Id, command, default);
        Assert.Equal(result, replay); Assert.Equal(3, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.NotNull((await f.Db.MiniAppReviewRequests.AsNoTracking().SingleAsync()).DecidedAt);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RejectAsync(current.Admin, current.Session, current.Review.Id,
            new(Guid.NewGuid(), result.ConcurrencyToken, "Another terminal decision is forbidden"), default));
        var app = await f.Db.MiniApps.AsNoTracking().SingleAsync();
        var next = await MiniAppVersionTests.Service(f).CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), app.Version, "2.0"), default);
        Assert.Equal(2, next.VersionCode); Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }
    [Fact]
    public async Task NonAssigneeAndUnstructuredOrUnknownChangesCannotDecide()
    {
        await using var f = await SandboxFixture.Create(); var current = await Started(f); var other = await MiniAppReviewAdminReadTests.Admin(f); var service = MiniAppReviewAssignmentTests.Service(f);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RejectAsync(other.Admin, other.Session, current.Review.Id,
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, "An unrelated admin cannot decide"), default));
        foreach (var command in new[] { new MiniAppReviewDecisionRequest(Guid.NewGuid(), current.Review.ConcurrencyToken, "short"),
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, "A valid but unstructured request"),
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, "A valid reason with an unknown field", [new("clientSecret", "Never collect credentials in review.")]) })
            await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RequestChangesAsync(current.Admin, current.Session, current.Review.Id, command, default));
        Assert.Equal(MiniAppReviewStatus.InReview, await f.Db.MiniAppReviewRequests.Select(x => x.Status).SingleAsync());
    }
    private sealed class ExpireAdmin(SandboxClock clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (e.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added && x.Entity.Action == MiniAppReviewActionKind.ReviewRejected)) clock.Now += TimeSpan.FromHours(2);
            return base.SavingChangesAsync(e, result, ct);
        }
    }
    [Fact]
    public async Task AdminExpiryDuringAuditRollsBackTheDecisionAndNotification()
    {
        await using var f = await SandboxFixture.Create(); var current = await Started(f);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).AddInterceptors(new ExpireAdmin(f.Clock)).Options);
        var service = new MiniAppReviewDecisionService(db, new(db, f.Clock), f.Clock);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.RejectAsync(current.Admin, current.Session, current.Review.Id,
            new(Guid.NewGuid(), current.Review.ConcurrencyToken, "A rejection which must roll back"), default));
        Assert.Equal(MiniAppReviewStatus.InReview, await f.Db.MiniAppReviewRequests.Select(x => x.Status).SingleAsync()); Assert.Equal(2, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
}
