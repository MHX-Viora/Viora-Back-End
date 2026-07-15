using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Notifications;

public enum NotificationError
{
    NotFound
}

public sealed record NotificationResult<T>(bool IsSuccess, T? Value, NotificationError? Error, string? Message)
{
    public static NotificationResult<T> Success(T value) => new(true, value, null, null);
    public static NotificationResult<T> Failure(NotificationError error, string message) => new(false, default, error, message);
}

public sealed record GetNotificationsQuery(
    Guid UserId,
    int Page,
    int PageSize,
    bool? IsRead,
    NotificationType? Type) : IRequest<NotificationListResponse>;

public sealed record MarkNotificationReadCommand(Guid UserId, Guid NotificationId)
    : IRequest<NotificationResult<MarkNotificationReadResponse>>;

public sealed record MarkAllNotificationsReadCommand(Guid UserId)
    : IRequest<MarkAllNotificationsReadResponse>;

public sealed record NotificationListResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    int UnreadCount,
    IReadOnlyList<NotificationItemResponse> Items);

public sealed record NotificationItemResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string? Content,
    string? ImageUrl,
    bool IsRead,
    DateTime CreatedAt,
    NotificationSenderResponse? Sender,
    NotificationReferenceResponse? Reference);

public sealed record NotificationSenderResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record NotificationReferenceResponse(
    Guid Id,
    NotificationReferenceType Type);

public sealed record MarkNotificationReadResponse(bool IsRead);
public sealed record MarkAllNotificationsReadResponse(int UpdatedCount);

public interface INotificationRepository
{
    Task<NotificationListResponse> GetNotificationsAsync(
        GetNotificationsQuery query,
        CancellationToken cancellationToken);

    Task<bool> ExistsForUserAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task MarkReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<int> MarkAllReadAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
