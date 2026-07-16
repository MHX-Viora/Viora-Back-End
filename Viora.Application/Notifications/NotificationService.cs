using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace Viora.Application.Notifications;

public sealed class NotificationService(
    INotificationDeliveryRepository repository,
    IRealtimeService realtimeService,
    IPushNotificationSender pushNotificationSender) : INotificationService
{
    public async Task<Notification> SendAsync(SendNotificationCommand command, CancellationToken cancellationToken)
    {
        var notification = NotificationFactory.Create(
            command.RecipientUserId,
            command.NotificationType,
            command.Sender,
            command.ReferenceId,
            command.ReferenceType,
            command.PostType,
            command.ImageUrl);

        if (notification.Id == Guid.Empty)
        {
            notification.Id = Guid.NewGuid();
        }

        await repository.AddAsync(notification, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await PublishAsync(notification, cancellationToken);
        return notification;
    }

    public async Task PublishAsync(Notification notification, CancellationToken cancellationToken)
    {
        var payload = new RealtimeNotificationPayload(
            notification.Id,
            notification.NotificationType,
            notification.ReferenceId,
            notification.ReferenceType,
            notification.Title,
            notification.Content,
            notification.ImageUrl,
            notification.CreatedAt);

        await realtimeService.SendToUserAsync(
            notification.UserId,
            RealtimeEvents.ReceiveNotification,
            payload,
            cancellationToken);

        try
        {
            await pushNotificationSender.SendAsync(new PushMessage(
                notification.UserId,
                notification.Title,
                notification.Content,
            BuildPushData(notification)), cancellationToken);
        }
        catch (Exception exception)
        {
            _ = exception;
        }
    }

    private static IReadOnlyDictionary<string, string> BuildPushData(Notification notification)
    {
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString(),
            ["notificationType"] = ((short)notification.NotificationType).ToString()
        };

        if (notification.ReferenceId.HasValue)
        {
            data["referenceId"] = notification.ReferenceId.Value.ToString();
        }

        if (notification.ReferenceType.HasValue)
        {
            data["referenceType"] = ((short)notification.ReferenceType.Value).ToString();
        }

        return data;
    }
}
