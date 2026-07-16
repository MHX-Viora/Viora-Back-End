using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Viora.Application.Realtime;

namespace Viora.Infrastructure.Realtime;

public sealed class SignalRRealtimeService(
    IHubContext<RealtimeHub> hubContext,
    ILogger<SignalRRealtimeService> logger) : IRealtimeService
{
    public async Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients.User(userId.ToString()).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send realtime event {EventName} to user {UserId}.", eventName, userId);
        }
    }

    public async Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken)
    {
        foreach (var userId in userIds.Distinct())
        {
            await SendToUserAsync(userId, eventName, payload, cancellationToken);
        }
    }

    public async Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients.Group(groupName).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send realtime event {EventName} to group {GroupName}.", eventName, groupName);
        }
    }
}
