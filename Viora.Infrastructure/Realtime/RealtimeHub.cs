using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Viora.Infrastructure.Realtime;

[Authorize]
public sealed class RealtimeHub(IConnectionRegistry connections) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId))
        {
            connections.Add(userId, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
        {
            connections.Remove(userId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task JoinGroup(string groupName) =>
        Groups.AddToGroupAsync(Context.ConnectionId, groupName);

    public Task LeaveGroup(string groupName) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
