using Viora.Application.Notifications;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class NotificationDeliveryRepository(AppDbContext dbContext) : INotificationDeliveryRepository
{
    public Task AddAsync(Notification notification, CancellationToken cancellationToken) =>
        dbContext.Notifications.AddAsync(notification, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
