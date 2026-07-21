using Microsoft.Extensions.Logging;
using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace Viora.Application.Notifications;

public sealed class NotificationService(
    INotificationDeliveryRepository repository,
    IRealtimeService realtimeService,
    IPushNotificationSender pushNotificationSender,
    ILogger<NotificationService> logger) : INotificationService
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
        logger.LogInformation(
            "Notification created. NotificationId: {NotificationId}, RecipientUserId: {RecipientUserId}, NotificationType: {NotificationType}, ReferenceId: {ReferenceId}, ReferenceType: {ReferenceType}.",
            notification.Id,
            notification.UserId,
            notification.NotificationType,
            notification.ReferenceId,
            notification.ReferenceType);

        await PublishAsync(notification, cancellationToken);
        return notification;
    }

    public async Task PublishAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (notification.CreatedAt == default)
        {
            notification.CreatedAt = DateTime.UtcNow;
        }

        var payload = MapNotificationResponse(notification);
        logger.LogInformation(
            "Notification dispatch started. NotificationId: {NotificationId}, RecipientUserId: {RecipientUserId}, NotificationType: {NotificationType}.",
            notification.Id,
            notification.UserId,
            notification.NotificationType);

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
                BuildPushData(payload)), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to publish push notification {NotificationId} for user {UserId}.",
                notification.Id,
                notification.UserId);
        }
    }

    private static NotificationItemResponse MapNotificationResponse(Notification notification) => new(
        notification.Id,
        notification.NotificationType,
        notification.Title,
        notification.Content,
        notification.ImageUrl,
        notification.IsRead,
        notification.CreatedAt,
        notification.SenderUser is null
            ? null
            : new NotificationSenderResponse(
                notification.SenderUser.Id,
                notification.SenderUser.DisplayName,
                notification.SenderUser.AvatarUrl,
                notification.SenderUser.IsVerified),
        notification.ReferenceId.HasValue && notification.ReferenceType.HasValue
            ? new NotificationReferenceResponse(
                notification.ReferenceId.Value,
                notification.ReferenceType.Value)
            : null);

    private static IReadOnlyDictionary<string, string> BuildPushData(NotificationItemResponse notification)
    {
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString(),
            ["id"] = notification.Id.ToString(),
            ["type"] = ((short)notification.Type).ToString(),
            ["notificationType"] = ((short)notification.Type).ToString(),
            ["title"] = notification.Title,
            ["content"] = notification.Content ?? string.Empty,
            ["imageUrl"] = notification.ImageUrl ?? string.Empty,
            ["isRead"] = notification.IsRead.ToString().ToLowerInvariant(),
            ["createdAt"] = notification.CreatedAt.ToUniversalTime().ToString("O"),
            ["createdAtUnixMs"] = new DateTimeOffset(notification.CreatedAt.ToUniversalTime()).ToUnixTimeMilliseconds().ToString()
        };

        if (notification.Sender is not null)
        {
            data["sender.id"] = notification.Sender.Id.ToString();
            data["sender.displayName"] = notification.Sender.DisplayName;
            data["sender.avatarUrl"] = notification.Sender.AvatarUrl ?? string.Empty;
            data["sender.isVerified"] = notification.Sender.IsVerified.ToString().ToLowerInvariant();
        }

        if (notification.Reference is not null)
        {
            data["reference.id"] = notification.Reference.Id.ToString();
            data["reference.type"] = ((short)notification.Reference.Type).ToString();
            data["referenceId"] = notification.Reference.Id.ToString();
            data["referenceType"] = ((short)notification.Reference.Type).ToString();
        }

        return data;
    }
}
