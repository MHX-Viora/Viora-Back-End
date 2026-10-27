using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewAssignmentTests
{
    internal static MiniAppReviewDecisionService Service(SandboxFixture f) => new(f.Db, new(f.Db, f.Clock), f.Clock);
    [Fact]
    public async Task StartClaimsOnceAndExplicitReassignmentChangesDecisionCapabilitiesWithoutDuplicatingEvents()
    {
        await using var f = await SandboxFixture.Create(); var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        var a = await MiniAppReviewAdminReadTests.Admin(f); var b = await MiniAppReviewAdminReadTests.Admin(f); var service = Service(f);
        var command = new MiniAppReviewCommand(Guid.NewGuid(), review.ConcurrencyToken);
        var started = await service.StartAsync(a.Admin, a.Session, review.Id, command, default);
        Assert.Equal("InReview", started.Status); Assert.Equal(started, await service.StartAsync(a.Admin, a.Session, review.Id, command, default));
        Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.StartAsync(b.Admin, b.Session, review.Id, new(Guid.NewGuid(), review.ConcurrencyToken), default))).Status);
        var assign = new MiniAppReviewAssignmentRequest(Guid.NewGuid(), started.ConcurrencyToken, b.Admin, "Explicit review reassignment");
        var assigned = await service.AssignAsync(a.Admin, a.Session, review.Id, assign, default);
        Assert.Equal(assigned, await service.AssignAsync(a.Admin, a.Session, review.Id, assign, default));
        var reads = new MiniAppReviewService(f.Db, new(f.Db, f.Clock));
        Assert.False((await reads.GetAdminAsync(a.Admin, a.Session, review.Id, default)).CanDecide);
        Assert.True((await reads.GetAdminAsync(b.Admin, b.Session, review.Id, default)).CanDecide);
        Assert.Equal(b.Admin, (await f.Db.MiniAppReviewRequests.AsNoTracking().SingleAsync()).AssignedAdminAccountId);
        Assert.Equal(2, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Single(await f.Db.MiniAppReviewActions.Where(x => x.Action == MiniAppReviewActionKind.ReviewAssigned).ToListAsync());
    }
    [Fact]
    public async Task ReassignmentRequiresAReasonAndALiveAdminTarget()
    {
        await using var f = await SandboxFixture.Create(); var request = await MiniAppReviewSubmissionTests.Request(f);
        var review = await MiniAppReviewSubmissionTests.Service(f).SubmitAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default);
        var a = await MiniAppReviewAdminReadTests.Admin(f); var service = Service(f);
        var started = await service.StartAsync(a.Admin, a.Session, review.Id, new(Guid.NewGuid(), review.ConcurrencyToken), default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.AssignAsync(a.Admin, a.Session, review.Id, new(Guid.NewGuid(), started.ConcurrencyToken, a.Admin, "short"), default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.AssignAsync(a.Admin, a.Session, review.Id, new(Guid.NewGuid(), started.ConcurrencyToken, Guid.NewGuid(), "A meaningful reassignment reason"), default));
        Assert.Equal(started.ConcurrencyToken, await f.Db.MiniAppReviewRequests.Select(x => x.ConcurrencyToken).SingleAsync());
    }
}
