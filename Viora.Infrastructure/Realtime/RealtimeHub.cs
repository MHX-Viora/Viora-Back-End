using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Realtime;

[Authorize]
public sealed class RealtimeHub(IConnectionRegistry connections, AppDbContext dbContext) : Hub
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

    public async Task JoinGroup(string groupName)
    {
        if (!TryGetUserId(out var userId) || !Guid.TryParse(groupName, out var conversationId))
        {
            throw new HubException("Conversation group is invalid.");
        }

        var canJoin = await dbContext.ConversationMembers.AsNoTracking().AnyAsync(member =>
            member.ConversationId == conversationId &&
            member.UserId == userId &&
            member.Status == ConversationMemberStatus.Active &&
            member.Conversation.DeletedAt == null);
        if (!canJoin) throw new HubException("You are not an active member of this conversation.");

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public Task LeaveGroup(string groupName) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
