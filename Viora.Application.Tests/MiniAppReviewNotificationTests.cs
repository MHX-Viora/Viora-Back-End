using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewNotificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InboxAndOutboxFollowTheAuditTransactionAndDeduplicateTheSameEvent(bool commit)
    {
        await using var f = await SandboxFixture.Create();
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            var action = await new MiniAppReviewAuditService(f.Db).AppendAsync(new()
            {
                MiniAppId = f.App.Id, Actor = new(MiniAppReviewActorType.Developer, f.Actor.Id), Action = MiniAppReviewActionKind.ReviewSubmitted,
                RequestId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), PayloadHash = MiniAppReviewAuditService.Hash(new { command = "submit-fixture" })
            }, default);
            var service = new MiniAppReviewNotificationService(f.Db, f.Clock);
            var first = await service.QueueAsync(action.Id, null, default);
            Assert.Equal(first.Id, (await service.QueueAsync(action.Id, null, default)).Id);
            Assert.Equal(f.App.DeveloperAccountId, first.RecipientDeveloperId); Assert.Equal(MiniAppReviewNotificationType.ReviewSubmitted, first.EventType);
            var delivery = await f.Db.MiniAppReviewNotificationDeliveries.SingleAsync();
            Assert.Equal(first.Id, delivery.NotificationId); Assert.Equal(0, delivery.AttemptCount);
            Assert.Equal(MiniAppNotificationDeliveryStatus.Pending, delivery.Status); Assert.Null(delivery.LeaseId); Assert.Null(delivery.SentAt);
            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        }
        Assert.Equal(commit ? 1 : 0, await f.Db.MiniAppReviewActions.CountAsync());
        Assert.Equal(commit ? 1 : 0, await f.Db.MiniAppReviewNotifications.CountAsync());
        Assert.Equal(commit ? 1 : 0, await f.Db.MiniAppReviewNotificationDeliveries.CountAsync());
        Assert.Empty(await f.Db.Notifications.ToListAsync()); // Developer recipients are not social users.
    }

    [Fact]
    public async Task QueueRequiresTheOwningTransactionAndADeliverableBusinessEvent()
    {
        await using var f = await SandboxFixture.Create(); var service = new MiniAppReviewNotificationService(f.Db, f.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(Guid.NewGuid(), null, default));
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        var action = await new MiniAppReviewAuditService(f.Db).AppendAsync(new()
        {
            MiniAppId = f.App.Id, Actor = new(MiniAppReviewActorType.Developer, f.Actor.Id), Action = MiniAppReviewActionKind.VersionCreated,
            RequestId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), PayloadHash = MiniAppReviewAuditService.Hash(new { command = "version-fixture" })
        }, default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.QueueAsync(action.Id, null, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.QueueAsync(Guid.NewGuid(), null, default));
        Assert.Empty(await f.Db.MiniAppReviewNotifications.ToListAsync()); Assert.Empty(await f.Db.MiniAppReviewNotificationDeliveries.ToListAsync());
    }
}
