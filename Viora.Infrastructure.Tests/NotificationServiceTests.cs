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
        var service = new NotificationService(new FakeNotificationDeliveryRepository(), realtime, push);
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            NotificationType = NotificationType.PostLike,
            ReferenceId = Guid.NewGuid(),
            ReferenceType = NotificationReferenceType.Post,
            Title = "Cam xuc",
            Content = "Someone liked your post.",
            CreatedAt = DateTime.UtcNow
        };

        await service.PublishAsync(notification, CancellationToken.None);

        var sent = Assert.Single(realtime.SentEvents);
        Assert.Equal(notification.UserId, sent.UserId);
        Assert.Equal(RealtimeEvents.ReceiveNotification, sent.EventName);
        var payload = Assert.IsType<RealtimeNotificationPayload>(sent.Payload);
        Assert.Equal(notification.Id, payload.NotificationId);
        Assert.Single(push.Messages);
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
