using MediatR;

namespace Viora.Application.Notifications;

public sealed class GetNotificationsHandler(INotificationRepository repository)
    : IRequestHandler<GetNotificationsQuery, NotificationListResponse>
{
    public Task<NotificationListResponse> Handle(
        GetNotificationsQuery request,
        CancellationToken cancellationToken) =>
        repository.GetNotificationsAsync(
            request with
            {
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 100)
            },
            cancellationToken);
}

public sealed class MarkNotificationReadHandler(INotificationRepository repository)
    : IRequestHandler<MarkNotificationReadCommand, NotificationResult<MarkNotificationReadResponse>>
{
    public async Task<NotificationResult<MarkNotificationReadResponse>> Handle(
        MarkNotificationReadCommand request,
        CancellationToken cancellationToken)
    {
        if (!await repository.ExistsForUserAsync(request.NotificationId, request.UserId, cancellationToken))
        {
            return NotificationResult<MarkNotificationReadResponse>.Failure(
                NotificationError.NotFound,
                "Khong tim thay thong bao.");
        }

        await repository.MarkReadAsync(request.NotificationId, request.UserId, cancellationToken);
        return NotificationResult<MarkNotificationReadResponse>.Success(new MarkNotificationReadResponse(true));
    }
}

public sealed class MarkAllNotificationsReadHandler(INotificationRepository repository)
    : IRequestHandler<MarkAllNotificationsReadCommand, MarkAllNotificationsReadResponse>
{
    public async Task<MarkAllNotificationsReadResponse> Handle(
        MarkAllNotificationsReadCommand request,
        CancellationToken cancellationToken)
    {
        var updatedCount = await repository.MarkAllReadAsync(request.UserId, cancellationToken);
        return new MarkAllNotificationsReadResponse(updatedCount);
    }
}
