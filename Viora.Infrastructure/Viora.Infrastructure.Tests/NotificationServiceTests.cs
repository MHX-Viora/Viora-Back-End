using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Notifications;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task Publish_sends_receive_notification_event()
    {
        var realtime = new FakeRealtimeService();
        var push = new FakePushNotificationSender();
        var service = new NotificationService(
            new FakeNotificationDeliveryRepository(),
            realtime,
            push,
            NullLogger<NotificationService>.Instance);
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            SenderUserId = Guid.NewGuid(),
            SenderUser = new User
            {
                Id = Guid.NewGuid(),
                DisplayName = "Sender",
                AvatarUrl = "https://example.test/avatar.png",
                IsVerified = true
            },
            NotificationType = NotificationType.PostLike,
            ReferenceId = Guid.NewGuid(),
            ReferenceType = NotificationReferenceType.Post,
            Title = "Cam xuc",
            Content = "Someone liked your post.",
            ImageUrl = "https://example.test/image.png",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await service.PublishAsync(notification, CancellationToken.None);

        var sent = Assert.Single(realtime.SentEvents);
        Assert.Equal(notification.UserId, sent.UserId);
        Assert.Equal(RealtimeEvents.ReceiveNotification, sent.EventName);
        var payload = Assert.IsType<NotificationItemResponse>(sent.Payload);
        Assert.Equal(notification.Id, payload.Id);
        Assert.Equal(notification.NotificationType, payload.Type);
        Assert.Equal(notification.Title, payload.Title);
        Assert.Equal(notification.Content, payload.Content);
        Assert.Equal(notification.ImageUrl, payload.ImageUrl);
        Assert.False(payload.IsRead);
        Assert.Equal(notification.CreatedAt, payload.CreatedAt);
        Assert.NotNull(payload.Sender);
        Assert.Equal(notification.SenderUser.Id, payload.Sender.Id);
        Assert.NotNull(payload.Reference);
        Assert.Equal(notification.ReferenceId, payload.Reference.Id);
        Assert.Equal(notification.ReferenceType, payload.Reference.Type);
        var pushMessage = Assert.Single(push.Messages);
        Assert.Equal(notification.Id.ToString(), pushMessage.Data["id"]);
        Assert.Equal(notification.Title, pushMessage.Data["title"]);
        var createdAt = DateTime.Parse(pushMessage.Data["createdAt"]).ToUniversalTime();
        Assert.Equal(notification.CreatedAt, createdAt);
        Assert.Equal(new DateTimeOffset(createdAt).ToUnixTimeSeconds().ToString(), pushMessage.Data["createdAtUnixSeconds"]);
        Assert.Equal(new DateTimeOffset(createdAt).ToUnixTimeMilliseconds().ToString(), pushMessage.Data["createdAtUnixMs"]);
        Assert.Equal(notification.SenderUser.DisplayName, pushMessage.Data["sender.displayName"]);
        Assert.Equal(notification.ReferenceId.ToString(), pushMessage.Data["reference.id"]);
    }

    [Fact]
    public async Task Publish_assigns_current_timestamp_when_notification_created_at_is_default()
    {
        var push = new FakePushNotificationSender();
        var service = new NotificationService(
            new FakeNotificationDeliveryRepository(),
            new FakeRealtimeService(),
            push,
            NullLogger<NotificationService>.Instance);
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            NotificationType = NotificationType.PostComment,
            Title = "Binh luan"
        };

        await service.PublishAsync(notification, CancellationToken.None);

        var pushMessage = Assert.Single(push.Messages);
        Assert.NotEqual(default, notification.CreatedAt);
        Assert.NotEqual("0001-01-01T00:00:00.0000000", pushMessage.Data["createdAt"]);
        Assert.True(long.Parse(pushMessage.Data["createdAtUnixSeconds"]) > 0);
        Assert.True(long.Parse(pushMessage.Data["createdAtUnixMs"]) > 0);
    }

    [Fact]
    public async Task Publish_treats_unspecified_created_at_as_utc_for_push_payload()
    {
        var push = new FakePushNotificationSender();
        var service = new NotificationService(
            new FakeNotificationDeliveryRepository(),
            new FakeRealtimeService(),
            push,
            NullLogger<NotificationService>.Instance);
        var createdAt = new DateTime(2026, 7, 22, 8, 30, 0, DateTimeKind.Unspecified);
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            NotificationType = NotificationType.PostComment,
            Title = "Binh luan",
            CreatedAt = createdAt
        };

        await service.PublishAsync(notification, CancellationToken.None);

        var pushMessage = Assert.Single(push.Messages);
        var expected = new DateTimeOffset(DateTime.SpecifyKind(createdAt, DateTimeKind.Utc));
        Assert.Equal("2026-07-22T08:30:00.0000000Z", pushMessage.Data["createdAt"]);
        Assert.Equal(expected.ToUnixTimeSeconds().ToString(), pushMessage.Data["createdAtUnixSeconds"]);
        Assert.Equal(expected.ToUnixTimeMilliseconds().ToString(), pushMessage.Data["createdAtUnixMs"]);
    }

    private sealed class FakeRealtimeService : IRealtimeService
    {
        public List<(Guid UserId, string EventName, object Payload)> SentEvents { get; } = [];

        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken)
        {
            SentEvents.Add((userId, eventName, payload));
            return Task.CompletedTask;
        }

        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakePushNotificationSender : IPushNotificationSender
    {
        public List<PushMessage> Messages { get; } = [];

        public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationDeliveryRepository : INotificationDeliveryRepository
    {
        public Task AddAsync(Notification notification, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
