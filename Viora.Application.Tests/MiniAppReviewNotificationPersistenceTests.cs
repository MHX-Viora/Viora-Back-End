using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Xunit;

public sealed class MiniAppReviewNotificationPersistenceTests
{
    [Fact]
    public async Task InboxAndEmailDeliveryAreUniqueAndBoundToTheDeveloperEvent()
    {
        await using var f = await SandboxFixture.Create();
        var action = MiniAppReviewPersistenceTests.Action(f.App);
        var inbox = Notification(f.App, action.Id);
        f.Db.AddRange(action, inbox); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Db.Add(Notification(f.App, action.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync()); f.Db.ChangeTracker.Clear();
        var delivery = Delivery(inbox); f.Db.Add(delivery); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Db.Add(Delivery(inbox)); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear(); var other = new DeveloperAccount { EmailNormalized = "inbox-other@example.test" };
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        var crossed = Notification(f.App, action.Id); crossed.RecipientDeveloperId = other.Id;
        f.Db.Add(crossed); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear(); var forged = Delivery(inbox); forged.EventId = Guid.NewGuid();
        f.Db.Add(forged); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.Empty(await f.Db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData("attempts")]
    [InlineData("lease")]
    [InlineData("sent")]
    [InlineData("channel")]
    [InlineData("status")]
    public async Task DeliveryHasBoundedAttemptsAndConsistentLeaseAndCompletion(string invalid)
    {
        await using var f = await SandboxFixture.Create();
        var action = MiniAppReviewPersistenceTests.Action(f.App); var inbox = Notification(f.App, action.Id);
        f.Db.AddRange(action, inbox); await f.Db.SaveChangesAsync();
        var delivery = Delivery(inbox);
        switch (invalid)
        {
            case "attempts": delivery.AttemptCount = 6; break;
            case "lease": delivery.Status = MiniAppNotificationDeliveryStatus.Processing; break;
            case "sent": delivery.Status = MiniAppNotificationDeliveryStatus.Sent; break;
            case "channel": delivery.Channel = (MiniAppNotificationChannel)99; break;
            case "status": delivery.Status = (MiniAppNotificationDeliveryStatus)99; break;
        }
        f.Db.Add(delivery); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    internal static MiniAppReviewNotification Notification(MiniApp app, Guid action) => new()
    {
        MiniAppId = app.Id, RecipientDeveloperId = app.DeveloperAccountId, EventId = action,
        EventType = MiniAppReviewNotificationType.ReviewSubmitted
    };
    internal static MiniAppReviewNotificationDelivery Delivery(MiniAppReviewNotification inbox) => new()
    {
        NotificationId = inbox.Id, EventId = inbox.EventId, RecipientDeveloperId = inbox.RecipientDeveloperId,
        NextAttemptAt = DateTime.UtcNow
    };
}
