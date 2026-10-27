using Microsoft.EntityFrameworkCore;
using Viora.Domain.Developers;
using Xunit;

public sealed class MiniAppReviewNotificationMigrationTests
{
    [Fact]
    public async Task InboxOutboxRoundTripAndReceiptIdentityIsImmutable()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var (previous, current, migrator) = await MiniAppReviewMigrationTests.Prepare(f.Db, "AddMiniAppReviewNotifications");
        var action = MiniAppReviewPersistenceTests.Action(f.App);
        var inbox = MiniAppReviewNotificationPersistenceTests.Notification(f.App, action.Id);
        var delivery = MiniAppReviewNotificationPersistenceTests.Delivery(inbox);
        f.Db.AddRange(action, inbox, delivery); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var duplicate = MiniAppReviewNotificationPersistenceTests.Delivery(inbox);
        f.Db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppReviewNotifications\" SET \"RecipientDeveloperId\"=gen_random_uuid() WHERE \"Id\"='{inbox.Id}'");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewNotifications\" SET \"ReadAt\"={DateTime.UtcNow} WHERE \"Id\"={inbox.Id}");
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppReviewNotifications\" SET \"ReadAt\"=NULL WHERE \"Id\"='{inbox.Id}'");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewNotificationDeliveries\" SET \"Status\"=1, \"AttemptCount\"=1, \"LeaseId\"={Guid.NewGuid()}, \"LeaseUntil\"={DateTime.UtcNow.AddMinutes(1)} WHERE \"Id\"={delivery.Id}");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewNotificationDeliveries\" SET \"Status\"=2, \"LeaseId\"=NULL, \"LeaseUntil\"=NULL, \"SentAt\"={DateTime.UtcNow} WHERE \"Id\"={delivery.Id}");
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppReviewNotificationDeliveries\" SET \"Status\"=0, \"SentAt\"=NULL WHERE \"Id\"='{delivery.Id}'");
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Single(await f.Db.MiniAppReviewActions.ToListAsync());
        Assert.Single(await f.Db.MiniAppOAuthClients.ToListAsync());
        Assert.Single(await f.Db.DeveloperSessions.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        Assert.Empty(await f.Db.MiniAppReviewNotifications.ToListAsync());
        Assert.Empty(await f.Db.MiniAppReviewNotificationDeliveries.ToListAsync());
        Assert.Empty(await f.Db.Notifications.ToListAsync());
    }
}
