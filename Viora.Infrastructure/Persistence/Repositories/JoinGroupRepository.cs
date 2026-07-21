using Microsoft.EntityFrameworkCore;
using Viora.Application.Chat;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class JoinGroupRepository(AppDbContext db) : IJoinGroupRepository
{
    public async Task<GroupChatResult<JoinGroupRepositoryResult>> JoinByInviteCodeAsync(JoinGroupCommand command, CancellationToken token)
    {
        var actor = await db.Users.AsNoTracking()
            .Where(user =>
                user.Id == command.CurrentUserId &&
                user.Account.Status == AccountStatus.Active &&
                user.Account.DeletedAt == null)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.AvatarUrl,
                user.IsVerified
            })
            .SingleOrDefaultAsync(token);

        if (actor is null)
        {
            return Fail(GroupChatError.NotFound, "Không tìm thấy người dùng.");
        }

        var group = await db.Conversations
            .Include(conversation => conversation.Members)
            .SingleOrDefaultAsync(conversation => conversation.InviteCode == command.InviteCode, token);

        if (group is null)
        {
            return Fail(GroupChatError.NotFound, "Không tìm thấy nhóm.");
        }

        if (group.ConversationType != ConversationType.Group)
        {
            return Fail(GroupChatError.Validation, "Mã mời không thuộc nhóm chat.");
        }

        if (group.DeletedAt.HasValue)
        {
            return Fail(GroupChatError.Validation, "Conversation has been dissolved.");
        }

        var existingMember = group.Members.SingleOrDefault(member => member.UserId == command.CurrentUserId);
        if (existingMember?.Status == ConversationMemberStatus.Active)
        {
            return Fail(GroupChatError.Conflict, "Bạn đã tham gia nhóm.");
        }

        if (existingMember?.Status == ConversationMemberStatus.Kicked)
        {
            return Fail(GroupChatError.Forbidden, "Bạn không có quyền tham gia nhóm này.");
        }

        var isBlocked = await db.ConversationBlocks.AsNoTracking().AnyAsync(block =>
            block.ConversationId == group.Id &&
            block.UserId == command.CurrentUserId,
            token);
        if (isBlocked)
        {
            return Fail(GroupChatError.Forbidden, "Bạn không có quyền tham gia nhóm này.");
        }

        var now = DateTime.UtcNow;
        var joinedBy = group.CreatedBy;
        if (existingMember is null)
        {
            group.Members.Add(new ConversationMember
            {
                Conversation = group,
                UserId = command.CurrentUserId,
                Role = ConversationMemberRole.Member,
                Status = ConversationMemberStatus.Active,
                JoinedAt = now,
                JoinedBy = joinedBy
            });
        }
        else
        {
            existingMember.Role = ConversationMemberRole.Member;
            existingMember.Status = ConversationMemberStatus.Active;
            existingMember.JoinedAt = now;
            existingMember.JoinedBy = joinedBy;
        }

        var systemMessage = new Message
        {
            Id = Guid.NewGuid(),
            Conversation = group,
            ConversationId = group.Id,
            SenderUserId = command.CurrentUserId,
            MessageType = MessageType.System,
            Content = GroupChatSystemMessages.Joined(actor.DisplayName),
            CreatedAt = now,
            UpdatedAt = now
        };
        group.LastMessageId = systemMessage.Id;
        group.LastMessageAt = now;
        group.UpdatedAt = now;

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            db.Messages.Add(systemMessage);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        var recipients = await db.ConversationMembers.AsNoTracking()
            .Where(member => member.ConversationId == group.Id && member.Status == ConversationMemberStatus.Active)
            .Select(member => member.UserId)
            .ToListAsync(token);
        var sender = new ChatMessageSenderResponse(actor.Id, actor.DisplayName, actor.AvatarUrl, actor.IsVerified);
        var realtimeMessage = GroupChatRealtimeMessages.CreateSystemMessage(
            systemMessage.Id,
            group.Id,
            sender,
            systemMessage.Content,
            systemMessage.CreatedAt,
            false);
        var response = new JoinGroupResponse(group.Id, true);
        var payload = new
        {
            conversationId = group.Id,
            user = new { actor.Id, actor.DisplayName, actor.AvatarUrl, actor.IsVerified },
            memberCount = recipients.Count
        };

        return GroupChatResult<JoinGroupRepositoryResult>.Success(new JoinGroupRepositoryResult(response, recipients, realtimeMessage, payload));
    }

    private static GroupChatResult<JoinGroupRepositoryResult> Fail(GroupChatError error, string message) =>
        GroupChatResult<JoinGroupRepositoryResult>.Failure(error, message);
}
