using Microsoft.EntityFrameworkCore;
using Viora.Application.Notifications;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository(AppDbContext dbContext) : INotificationRepository
{
    public async Task<NotificationListResponse> GetNotificationsAsync(
        GetNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var notifications = dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == query.UserId);

        if (query.IsRead.HasValue)
        {
            notifications = notifications.Where(notification => notification.IsRead == query.IsRead.Value);
        }

        if (query.Type.HasValue)
        {
            notifications = notifications.Where(notification => notification.NotificationType == query.Type.Value);
        }

        var totalItems = await notifications.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var unreadCount = await dbContext.Notifications
            .AsNoTracking()
            .CountAsync(
                notification => notification.UserId == query.UserId && !notification.IsRead,
                cancellationToken);

        var items = await notifications
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(notification => new NotificationItemResponse(
                notification.Id,
                notification.NotificationType,
                notification.Title,
                notification.Content,
                notification.ImageUrl,
                notification.IsRead,
                notification.CreatedAt,
                notification.NotificationType == NotificationType.System || notification.SenderUser == null
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
                    : null))
            .ToListAsync(cancellationToken);

        return new NotificationListResponse(page, pageSize, totalItems, totalPages, unreadCount, items);
    }

    public Task<bool> ExistsForUserAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Notifications
            .AsNoTracking()
            .AnyAsync(
                notification => notification.Id == notificationId && notification.UserId == userId,
                cancellationToken);

    public Task MarkReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Notifications
            .Where(notification =>
                notification.Id == notificationId &&
                notification.UserId == userId &&
                !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.IsRead, true),
                cancellationToken);

    public Task<int> MarkAllReadAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.IsRead, true),
                cancellationToken);
}
