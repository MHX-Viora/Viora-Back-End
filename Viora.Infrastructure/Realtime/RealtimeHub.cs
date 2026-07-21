using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Realtime;

[Authorize]
public sealed class RealtimeHub(
    IConnectionRegistry connections,
    AppDbContext dbContext,
    ILogger<RealtimeHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId))
        {
            connections.Add(userId, Context.ConnectionId);
            logger.LogInformation(
                "Realtime connected. UserId: {UserId}, ConnectionId: {ConnectionId}.",
                userId,
                Context.ConnectionId);
        }
        else
        {
            logger.LogWarning(
                "Realtime connected without valid user_id claim. ConnectionId: {ConnectionId}.",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
        {
            connections.Remove(userId, Context.ConnectionId);
            logger.LogInformation(
                "Realtime disconnected. UserId: {UserId}, ConnectionId: {ConnectionId}, Error: {Error}.",
                userId,
                Context.ConnectionId,
                exception?.Message);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGroup(string groupName)
    {
        if (!TryGetUserId(out var userId) || !TryParseConversationGroupName(groupName, out var conversationId))
        {
            logger.LogWarning(
                "Realtime join group rejected because group name is invalid. GroupName: {GroupName}, ConnectionId: {ConnectionId}.",
                groupName,
                Context.ConnectionId);
            throw new HubException("Conversation group is invalid.");
        }

        var canJoin = await dbContext.ConversationMembers.AsNoTracking().AnyAsync(member =>
            member.ConversationId == conversationId &&
            member.UserId == userId &&
            member.Status == ConversationMemberStatus.Active &&
            member.Conversation.DeletedAt == null);
        if (!canJoin)
        {
            logger.LogWarning(
                "Realtime join group rejected because user is not an active member. UserId: {UserId}, ConversationId: {ConversationId}, ConnectionId: {ConnectionId}.",
                userId,
                conversationId,
                Context.ConnectionId);
            throw new HubException("You are not an active member of this conversation.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        logger.LogInformation(
            "Realtime joined group. UserId: {UserId}, ConversationId: {ConversationId}, GroupName: {GroupName}, ConnectionId: {ConnectionId}.",
            userId,
            conversationId,
            groupName,
            Context.ConnectionId);
    }

    public Task LeaveGroup(string groupName) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out userId);
    }

    private static bool TryParseConversationGroupName(string groupName, out Guid conversationId)
    {
        const string Prefix = "conversation:";

        if (groupName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            groupName = groupName[Prefix.Length..];
        }

        return Guid.TryParse(groupName, out conversationId);
    }
}
