using Microsoft.EntityFrameworkCore;
using Viora.Application.Calls;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class CallHistoryMessageRepository(AppDbContext dbContext)
    : ICallHistoryMessageRepository
{
    public async Task<CallHistoryMessage?> CreateAsync(
        CallSessionResponse call,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Messages
                .AsNoTracking()
                .AnyAsync(message => message.Id == call.Id, cancellationToken))
        {
            return null;
        }

        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(value => value.Id == call.ConversationId, cancellationToken);
        var sender = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == call.Caller.Id)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.AvatarUrl,
                user.IsVerified
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null || sender is null) return null;

        var createdAt = call.EndedAt ?? DateTime.UtcNow;
        var content = CallHistoryMessages.Format(call.CallType, call.Status, call.Duration);
        var message = new Message
        {
            Id = call.Id,
            Conversation = conversation,
            ConversationId = conversation.Id,
            SenderUserId = sender.Id,
            MessageType = MessageType.System,
            Content = content,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        dbContext.Messages.Add(message);
        conversation.LastMessageId = message.Id;
        conversation.LastMessageAt = createdAt;
        conversation.UpdatedAt = createdAt;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CallHistoryMessage(
            message.Id,
            message.ConversationId,
            new CallParticipantResponse(sender.Id, sender.DisplayName, sender.AvatarUrl),
            sender.IsVerified,
            content,
            createdAt);
    }
}
