using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Chat;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class ChatConversationRepository(AppDbContext dbContext) : IChatConversationRepository
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string VietnameseDiacritics =
        "àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ";

    private const string VietnameseAscii =
        "aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd";

    public async Task<ChatConversationListResponse> GetConversationsAsync(
        GetChatConversationsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var skip = (page - 1) * pageSize;
        var keyword = string.IsNullOrWhiteSpace(query.Keyword)
            ? null
            : RemoveDiacritics(query.Keyword.Trim()).ToLowerInvariant();

        var conversations = dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.UserId == query.UserId &&
                member.Status == ConversationMemberStatus.Active)
            .Select(member => new
            {
                member.ConversationId,
                member.IsMuted,
                member.IsPinned,
                member.LastReadMessageId,
                ConversationType = member.Conversation.ConversationType,
                GroupName = member.Conversation.Name,
                GroupAvatarUrl = member.Conversation.AvatarUrl,
                SortAt = member.Conversation.LastMessageAt ?? member.Conversation.CreatedAt,
                LastMessageAt = member.Conversation.LastMessageAt,
                OtherMember = member.Conversation.Members
                    .Where(other =>
                        other.UserId != query.UserId &&
                        other.Status == ConversationMemberStatus.Active)
                    .OrderBy(other => other.JoinedAt)
                    .Select(other => new
                    {
                        other.User.Id,
                        other.User.DisplayName,
                        other.User.AvatarUrl
                    })
                    .FirstOrDefault(),
                MemberCount = member.Conversation.Members
                    .Count(other => other.Status == ConversationMemberStatus.Active),
                LastReadCreatedAt = member.LastReadMessage == null
                    ? (DateTime?)null
                    : member.LastReadMessage.CreatedAt,
                LastMessage = member.Conversation.LastMessage == null
                    ? null
                    : new ChatLastMessageResponse(
                        member.Conversation.LastMessage.Id,
                        member.Conversation.LastMessage.SenderUserId,
                        member.Conversation.LastMessage.SenderUser.DisplayName,
                        member.Conversation.LastMessage.MessageType,
                        member.Conversation.LastMessage.Content,
                        Array.Empty<ChatMessageAttachmentResponse>(),
                        member.Conversation.LastMessage.CreatedAt,
                        member.Conversation.LastMessage.SenderUserId == query.UserId),
                UnreadCount = member.Conversation.Messages.Count(message =>
                    message.SenderUserId != query.UserId &&
                    (member.LastReadMessageId == null ||
                     message.CreatedAt > member.LastReadMessage!.CreatedAt))
            });

        if (keyword is not null)
        {
            conversations = conversations.Where(conversation =>
                conversation.ConversationType == ConversationType.Private
                    ? conversation.OtherMember != null &&
                      AppDbContext.Translate(
                          conversation.OtherMember.DisplayName.ToLower(),
                          VietnameseDiacritics,
                          VietnameseAscii).Contains(keyword)
                    : conversation.GroupName != null &&
                      AppDbContext.Translate(
                          conversation.GroupName.ToLower(),
                          VietnameseDiacritics,
                          VietnameseAscii).Contains(keyword));
        }

        var totalItems = await conversations.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await conversations
            .OrderByDescending(conversation => conversation.SortAt)
            .ThenByDescending(conversation => conversation.ConversationId)
            .Skip(skip)
            .Take(pageSize)
            .Select(conversation => new ChatConversationItemResponse(
                conversation.ConversationId,
                conversation.ConversationType,
                conversation.ConversationType == ConversationType.Private
                    ? conversation.OtherMember == null ? null : conversation.OtherMember.DisplayName
                    : conversation.GroupName,
                conversation.ConversationType == ConversationType.Private
                    ? conversation.OtherMember == null ? null : conversation.OtherMember.AvatarUrl
                    : conversation.GroupAvatarUrl,
                conversation.ConversationType == ConversationType.Private && conversation.OtherMember != null
                    ? new ChatParticipantResponse(
                        conversation.OtherMember.Id,
                        conversation.OtherMember.DisplayName,
                        conversation.OtherMember.AvatarUrl)
                    : null,
                conversation.MemberCount,
                conversation.LastMessage,
                conversation.UnreadCount,
                conversation.IsMuted,
                conversation.IsPinned,
                conversation.LastMessageAt))
            .ToListAsync(cancellationToken);

        await PopulateLastMessageAttachmentsAsync(items, cancellationToken);

        return new ChatConversationListResponse(page, pageSize, totalItems, totalPages, items);
    }

    public async Task<ChatResult<ChatMessageListResponse>> GetMessagesAsync(
        GetChatConversationMessagesQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var conversationExists = await dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(conversation => conversation.Id == query.ConversationId, cancellationToken);
        if (!conversationExists)
        {
            return ChatResult<ChatMessageListResponse>.Failure(
                ChatError.ConversationNotFound,
                "Khong tim thay cuoc tro chuyen.");
        }

        var isActiveMember = await dbContext.ConversationMembers
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == query.ConversationId &&
                member.UserId == query.UserId &&
                member.Status == ConversationMemberStatus.Active,
                cancellationToken);
        if (!isActiveMember)
        {
            return ChatResult<ChatMessageListResponse>.Failure(
                ChatError.Forbidden,
                "Ban khong co quyen xem cuoc tro chuyen nay.");
        }

        var conversation = await dbContext.Conversations
            .AsNoTracking()
            .Where(value => value.Id == query.ConversationId)
            .Select(value => new
            {
                value.Id,
                value.ConversationType
            })
            .FirstAsync(cancellationToken);

        var blockedBy = conversation.ConversationType == ConversationType.Private
            ? await dbContext.ConversationBlocks
                .AsNoTracking()
                .Where(block => block.ConversationId == query.ConversationId)
                .OrderBy(block => block.CreatedAt)
                .Select(block => new ChatParticipantResponse(
                    block.User.Id,
                    block.User.DisplayName,
                    block.User.AvatarUrl))
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var conversationResponse = new ChatMessageConversationResponse(
            conversation.Id,
            conversation.ConversationType,
            conversation.ConversationType == ConversationType.Private && blockedBy is not null,
            blockedBy);

        var messages = dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == query.ConversationId);

        var totalItems = await messages.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var pageMessages = await messages
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(message => new
            {
                message.Id,
                Sender = new ChatMessageSenderResponse(
                    message.SenderUser.Id,
                    message.SenderUser.DisplayName,
                    message.SenderUser.AvatarUrl,
                    message.SenderUser.IsVerified),
                message.MessageType,
                message.Content,
                ReplyMessage = message.ReplyMessage == null
                    ? null
                    : new ChatReplyMessageResponse(
                        message.ReplyMessage.Id,
                        message.ReplyMessage.Content,
                        message.ReplyMessage.MessageType,
                        message.ReplyMessage.SenderUser.DisplayName),
                Attachments = Array.Empty<ChatMessageAttachmentResponse>(),
                IsMine = message.SenderUserId == query.UserId,
                message.IsEdited,
                message.IsDeleted,
                message.CreatedAt,
                message.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var messageIds = pageMessages.Select(message => message.Id).ToArray();
        var pageAttachments = await dbContext.MessageAttachments
            .AsNoTracking()
            .Where(attachment => messageIds.Contains(attachment.MessageId))
            .OrderBy(attachment => attachment.Id)
            .Select(attachment => new
            {
                attachment.MessageId,
                Attachment = new ChatMessageAttachmentResponse(
                    attachment.Id,
                    attachment.FileUrl,
                    attachment.FileName,
                    attachment.MimeType,
                    attachment.ThumbnailUrl,
                    attachment.FileSize,
                    attachment.Duration)
            })
            .ToListAsync(cancellationToken);
        var attachmentsByMessage = pageAttachments
            .GroupBy(attachment => attachment.MessageId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(attachment => attachment.Attachment).ToList());

        var pageReactions = await dbContext.MessageReactions
            .AsNoTracking()
            .Where(reaction => messageIds.Contains(reaction.MessageId))
            .OrderBy(reaction => reaction.CreatedAt)
            .Select(reaction => new
            {
                reaction.MessageId,
                Reaction = new ChatMessageReactionResponse(
                    reaction.UserId,
                    reaction.User.DisplayName,
                    reaction.ReactionType)
            })
            .ToListAsync(cancellationToken);
        var reactionsByMessage = pageReactions
            .GroupBy(reaction => reaction.MessageId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(reaction => reaction.Reaction).ToList());

        var items = pageMessages
            .Select(message =>
            {
                attachmentsByMessage.TryGetValue(message.Id, out var attachments);
                attachments ??= [];
                if (message.MessageType == MessageType.Recall)
                {
                    attachments = [];
                }
                reactionsByMessage.TryGetValue(message.Id, out var reactions);
                reactions ??= [];

                return new ChatMessageItemResponse(
                    message.Id,
                    message.Sender,
                    message.MessageType,
                    message.Content,
                    message.ReplyMessage,
                    attachments,
                    reactions,
                    BuildReactionSummary(reactions),
                    message.IsMine,
                    message.IsEdited,
                    message.IsDeleted,
                    message.CreatedAt,
                    message.UpdatedAt);
            })
            .ToList();

        items.Reverse();

        return ChatResult<ChatMessageListResponse>.Success(
            new ChatMessageListResponse(page, pageSize, totalItems, totalPages, conversationResponse, items));
    }

    public async Task<ChatConversationItemResponse?> GetConversationItemAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.UserId == userId &&
                member.ConversationId == conversationId &&
                member.Status == ConversationMemberStatus.Active)
            .Select(member => new
            {
                member.ConversationId,
                member.IsMuted,
                member.IsPinned,
                ConversationType = member.Conversation.ConversationType,
                GroupName = member.Conversation.Name,
                GroupAvatarUrl = member.Conversation.AvatarUrl,
                LastMessageAt = member.Conversation.LastMessageAt,
                OtherMember = member.Conversation.Members
                    .Where(other =>
                        other.UserId != userId &&
                        other.Status == ConversationMemberStatus.Active)
                    .OrderBy(other => other.JoinedAt)
                    .Select(other => new
                    {
                        other.User.Id,
                        other.User.DisplayName,
                        other.User.AvatarUrl
                    })
                    .FirstOrDefault(),
                MemberCount = member.Conversation.Members
                    .Count(other => other.Status == ConversationMemberStatus.Active),
                LastMessage = member.Conversation.LastMessage == null
                    ? null
                    : new ChatLastMessageResponse(
                        member.Conversation.LastMessage.Id,
                        member.Conversation.LastMessage.SenderUserId,
                        member.Conversation.LastMessage.SenderUser.DisplayName,
                        member.Conversation.LastMessage.MessageType,
                        member.Conversation.LastMessage.Content,
                        Array.Empty<ChatMessageAttachmentResponse>(),
                        member.Conversation.LastMessage.CreatedAt,
                        member.Conversation.LastMessage.SenderUserId == userId),
                UnreadCount = member.Conversation.Messages.Count(message =>
                    message.SenderUserId != userId &&
                    (member.LastReadMessageId == null ||
                     message.CreatedAt > member.LastReadMessage!.CreatedAt))
            })
            .Select(conversation => new ChatConversationItemResponse(
                conversation.ConversationId,
                conversation.ConversationType,
                conversation.ConversationType == ConversationType.Private
                    ? conversation.OtherMember == null ? null : conversation.OtherMember.DisplayName
                    : conversation.GroupName,
                conversation.ConversationType == ConversationType.Private
                    ? conversation.OtherMember == null ? null : conversation.OtherMember.AvatarUrl
                    : conversation.GroupAvatarUrl,
                conversation.ConversationType == ConversationType.Private && conversation.OtherMember != null
                    ? new ChatParticipantResponse(
                        conversation.OtherMember.Id,
                        conversation.OtherMember.DisplayName,
                        conversation.OtherMember.AvatarUrl)
                    : null,
                conversation.MemberCount,
                conversation.LastMessage,
                conversation.UnreadCount,
                conversation.IsMuted,
                conversation.IsPinned,
                conversation.LastMessageAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (item is not null)
        {
            await PopulateLastMessageAttachmentsAsync([item], cancellationToken);
        }

        return item;
    }

    public async Task<ChatResult<SendChatMessageRepositoryResult>> SendMessageAsync(
        SendChatMessageCommand command,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(value => value.Id == command.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return ChatResult<SendChatMessageRepositoryResult>.Failure(
                ChatError.ConversationNotFound,
                "Khong tim thay cuoc tro chuyen.");
        }

        var senderMember = await dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.ConversationId == command.ConversationId &&
                member.UserId == command.SenderUserId &&
                member.Status == ConversationMemberStatus.Active)
            .Select(member => new
            {
                member.UserId,
                member.Role,
                member.User.DisplayName,
                member.User.AvatarUrl,
                member.User.IsVerified
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (senderMember is null)
        {
            return ChatResult<SendChatMessageRepositoryResult>.Failure(
                ChatError.Forbidden,
                "Ban khong co quyen gui tin nhan trong cuoc tro chuyen nay.");
        }

        if (conversation.ConversationType == ConversationType.Group &&
            !CanSendToGroup(conversation.CanSendMessage, senderMember.Role))
        {
            return ChatResult<SendChatMessageRepositoryResult>.Failure(
                ChatError.Forbidden,
                "Ban khong co quyen gui tin nhan trong nhom nay.");
        }

        var isBlocked = await dbContext.ConversationBlocks
            .AsNoTracking()
            .AnyAsync(block => block.ConversationId == command.ConversationId, cancellationToken);
        if (isBlocked)
        {
            return ChatResult<SendChatMessageRepositoryResult>.Failure(
                ChatError.Forbidden,
                "Cuoc tro chuyen dang bi chan.");
        }

        ChatReplyMessageResponse? replyMessage = null;
        if (command.ReplyMessageId.HasValue)
        {
            replyMessage = await dbContext.Messages
                .AsNoTracking()
                .Where(message =>
                    message.Id == command.ReplyMessageId.Value &&
                    message.ConversationId == command.ConversationId)
                .Select(message => new ChatReplyMessageResponse(
                    message.Id,
                    message.Content,
                    message.MessageType,
                    message.SenderUser.DisplayName))
                .FirstOrDefaultAsync(cancellationToken);
            if (replyMessage is null)
            {
                return ChatResult<SendChatMessageRepositoryResult>.Failure(
                    ChatError.MessageNotFound,
                    "Tin nhan reply khong ton tai trong cuoc tro chuyen nay.");
            }
        }

        var now = DateTime.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = command.ConversationId,
            SenderUserId = command.SenderUserId,
            ReplyMessageId = command.ReplyMessageId,
            MessageType = command.MessageType,
            Content = command.Content,
            IsEdited = false,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        var attachments = (command.Attachments ?? [])
            .Select(attachment => new MessageAttachment
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                FileUrl = attachment.FileUrl,
                FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? null : attachment.FileName.Trim(),
                MimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? null : attachment.MimeType.Trim(),
                ThumbnailUrl = string.IsNullOrWhiteSpace(attachment.ThumbnailUrl) ? null : attachment.ThumbnailUrl.Trim(),
                FileSize = attachment.FileSize,
                Duration = attachment.Duration
            })
            .ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Messages.Add(message);
        if (attachments.Count > 0)
        {
            dbContext.MessageAttachments.AddRange(attachments);
        }

        conversation.LastMessageId = message.Id;
        conversation.LastMessageAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = new SendChatMessageResponse(
            message.Id,
            message.ConversationId,
            new ChatMessageSenderResponse(
                senderMember.UserId,
                senderMember.DisplayName,
                senderMember.AvatarUrl,
                senderMember.IsVerified),
            message.MessageType,
            message.Content,
            replyMessage,
            attachments
                .OrderBy(attachment => attachment.Id)
                .Select(attachment => new ChatMessageAttachmentResponse(
                    attachment.Id,
                    attachment.FileUrl,
                    attachment.FileName,
                    attachment.MimeType,
                    attachment.ThumbnailUrl,
                    attachment.FileSize,
                    attachment.Duration))
                .ToList(),
            true,
            false,
            false,
            message.CreatedAt);

        var recipients = await BuildRecipientStatesAsync(command.ConversationId, cancellationToken);

        return ChatResult<SendChatMessageRepositoryResult>.Success(
            new SendChatMessageRepositoryResult(response, recipients));
    }

    public async Task<ChatResult<MarkConversationReadRepositoryResult>> MarkReadAsync(
        MarkConversationReadCommand command,
        CancellationToken cancellationToken)
    {
        var readAt = DateTime.UtcNow;

        var conversation = await dbContext.Conversations
            .AsNoTracking()
            .Where(value => value.Id == command.ConversationId)
            .Select(value => new
            {
                value.Id,
                value.LastMessageId
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return ChatResult<MarkConversationReadRepositoryResult>.Failure(
                ChatError.ConversationNotFound,
                "Khong tim thay cuoc tro chuyen.");
        }

        var membership = await dbContext.ConversationMembers
            .Where(member =>
                member.ConversationId == command.ConversationId &&
                member.UserId == command.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (membership?.Status != ConversationMemberStatus.Active)
        {
            return ChatResult<MarkConversationReadRepositoryResult>.Failure(
                ChatError.Forbidden,
                "Ban khong co quyen danh dau da doc cuoc tro chuyen nay.");
        }

        var memberIds = await dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.ConversationId == command.ConversationId &&
                member.Status == ConversationMemberStatus.Active)
            .Select(member => member.UserId)
            .ToListAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var didUpdate = false;
        if (conversation.LastMessageId.HasValue &&
            membership.LastReadMessageId != conversation.LastMessageId.Value)
        {
            membership.LastReadMessageId = conversation.LastMessageId.Value;
            membership.LastReadAt = readAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            didUpdate = true;
        }

        await transaction.CommitAsync(cancellationToken);

        var response = new MarkConversationReadResponse(
            command.ConversationId,
            conversation.LastMessageId,
            readAt);

        return ChatResult<MarkConversationReadRepositoryResult>.Success(
            new MarkConversationReadRepositoryResult(response, memberIds, didUpdate));
    }

    public async Task<ChatResult<RecallChatMessageRepositoryResult>> RecallMessageAsync(
        RecallChatMessageCommand command,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages
            .FirstOrDefaultAsync(value => value.Id == command.MessageId, cancellationToken);
        if (message is null)
        {
            return ChatResult<RecallChatMessageRepositoryResult>.Failure(
                ChatError.MessageNotFound,
                "Khong tim thay tin nhan.");
        }

        var isActiveMember = await dbContext.ConversationMembers
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == message.ConversationId &&
                member.UserId == command.UserId &&
                member.Status == ConversationMemberStatus.Active,
                cancellationToken);
        if (!isActiveMember || message.SenderUserId != command.UserId)
        {
            return ChatResult<RecallChatMessageRepositoryResult>.Failure(
                ChatError.Forbidden,
                "Ban khong co quyen thu hoi tin nhan nay.");
        }

        var memberIds = await dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.ConversationId == message.ConversationId &&
                member.Status == ConversationMemberStatus.Active)
            .Select(member => member.UserId)
            .ToListAsync(cancellationToken);

        var deletedAt = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        message.MessageType = MessageType.Recall;
        message.Content = "Tin nhan da duoc thu hoi.";
        message.IsDeleted = true;
        message.IsEdited = false;
        message.UpdatedAt = deletedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = new RecallChatMessageResponse(
            message.ConversationId,
            message.Id,
            command.UserId,
            deletedAt);

        return ChatResult<RecallChatMessageRepositoryResult>.Success(
            new RecallChatMessageRepositoryResult(response, memberIds));
    }

    public async Task<ChatResult<SetConversationPinResponse>> SetPinAsync(
        SetConversationPinCommand command,
        CancellationToken cancellationToken)
    {
        var conversationExists = await dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(conversation => conversation.Id == command.ConversationId, cancellationToken);
        if (!conversationExists)
        {
            return ChatResult<SetConversationPinResponse>.Failure(
                ChatError.ConversationNotFound,
                "Khong tim thay cuoc tro chuyen.");
        }

        var isActiveMember = await dbContext.ConversationMembers
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == command.ConversationId &&
                member.UserId == command.UserId &&
                member.Status == ConversationMemberStatus.Active,
                cancellationToken);
        if (!isActiveMember)
        {
            return ChatResult<SetConversationPinResponse>.Failure(
                ChatError.Forbidden,
                "Ban khong co quyen ghim cuoc tro chuyen nay.");
        }

        await dbContext.ConversationMembers
            .Where(member =>
                member.ConversationId == command.ConversationId &&
                member.UserId == command.UserId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.IsPinned, command.IsPinned),
                cancellationToken);

        return ChatResult<SetConversationPinResponse>.Success(
            new SetConversationPinResponse(command.ConversationId, command.IsPinned));
    }

    public async Task<ChatResult<SetConversationMuteResponse>> SetMuteAsync(
        SetConversationMuteCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateActiveMemberAsync(command.ConversationId, command.UserId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ChatResult<SetConversationMuteResponse>.Failure(validation.Error!.Value, validation.Message!);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ConversationMembers
            .Where(member => member.ConversationId == command.ConversationId && member.UserId == command.UserId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.IsMuted, command.IsMuted),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ChatResult<SetConversationMuteResponse>.Success(
            new SetConversationMuteResponse(command.ConversationId, command.IsMuted));
    }

    public async Task<ChatResult<SetConversationBlockResponse>> SetBlockAsync(
        SetConversationBlockCommand command,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations
            .AsNoTracking()
            .Where(value => value.Id == command.ConversationId)
            .Select(value => new { value.Id, value.ConversationType })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return ChatResult<SetConversationBlockResponse>.Failure(ChatError.ConversationNotFound, "Khong tim thay cuoc tro chuyen.");
        }

        if (conversation.ConversationType != ConversationType.Private)
        {
            return ChatResult<SetConversationBlockResponse>.Failure(ChatError.Validation, "Chi co the chan cuoc tro chuyen rieng.");
        }

        var validation = await ValidateActiveMemberAsync(command.ConversationId, command.UserId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ChatResult<SetConversationBlockResponse>.Failure(validation.Error!.Value, validation.Message!);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existing = await dbContext.ConversationBlocks
            .FirstOrDefaultAsync(block =>
                block.ConversationId == command.ConversationId &&
                block.UserId == command.UserId,
                cancellationToken);

        if (command.IsBlocked && existing is null)
        {
            dbContext.ConversationBlocks.Add(new ConversationBlock
            {
                ConversationId = command.ConversationId,
                UserId = command.UserId,
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (!command.IsBlocked && existing is not null)
        {
            dbContext.ConversationBlocks.Remove(existing);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ChatResult<SetConversationBlockResponse>.Success(
            new SetConversationBlockResponse(command.ConversationId, command.IsBlocked));
    }

    public async Task<ChatResult<ChatConversationInfoResponse>> GetInfoAsync(
        GetConversationInfoQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateActiveMemberAsync(query.ConversationId, query.UserId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ChatResult<ChatConversationInfoResponse>.Failure(validation.Error!.Value, validation.Message!);
        }

        var info = await dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member => member.ConversationId == query.ConversationId && member.UserId == query.UserId)
            .Select(member => new
            {
                member.ConversationId,
                member.IsPinned,
                member.IsMuted,
                member.Conversation.ConversationType,
                member.Conversation.Name,
                member.Conversation.AvatarUrl,
                member.Conversation.CanSendMessage,
                member.Conversation.CreatedBy,
                OtherMember = member.Conversation.Members
                    .Where(other =>
                        member.Conversation.ConversationType == ConversationType.Private &&
                        other.UserId != query.UserId &&
                        other.Status == ConversationMemberStatus.Active)
                    .Select(other => new
                    {
                        other.UserId,
                        other.User.DisplayName,
                        other.User.AvatarUrl,
                        other.User.IsVerified
                    })
                    .FirstOrDefault(),
                MemberCount = member.Conversation.Members.Count(other => other.Status == ConversationMemberStatus.Active),
                IsBlocked = dbContext.ConversationBlocks.Any(block =>
                    block.ConversationId == query.ConversationId &&
                    block.UserId == query.UserId)
            })
            .FirstAsync(cancellationToken);

        return ChatResult<ChatConversationInfoResponse>.Success(new ChatConversationInfoResponse(
            info.ConversationId,
            info.ConversationType,
            info.ConversationType == ConversationType.Private ? info.OtherMember?.DisplayName : info.Name,
            info.ConversationType == ConversationType.Private ? info.OtherMember?.AvatarUrl : info.AvatarUrl,
            info.MemberCount,
            info.IsPinned,
            info.IsMuted,
            info.IsBlocked,
            info.ConversationType == ConversationType.Group ? info.CanSendMessage : null,
            info.ConversationType == ConversationType.Group ? info.CreatedBy : null,
            info.ConversationType == ConversationType.Private && info.OtherMember is not null
                ? new ChatConversationOtherUserResponse(
                    info.OtherMember.UserId,
                    info.OtherMember.DisplayName,
                    info.OtherMember.AvatarUrl,
                    info.OtherMember.IsVerified)
                : null));
    }

    public async Task<ChatResult<ChatAttachmentListResponse>> GetAttachmentsAsync(
        GetConversationAttachmentsQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateActiveMemberAsync(query.ConversationId, query.UserId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ChatResult<ChatAttachmentListResponse>.Failure(validation.Error!.Value, validation.Message!);
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var attachments = dbContext.MessageAttachments
            .AsNoTracking()
            .Where(attachment =>
                attachment.Message.ConversationId == query.ConversationId &&
                !attachment.Message.IsDeleted &&
                attachment.Message.MessageType != MessageType.Recall);

        attachments = query.Type switch
        {
            ChatAttachmentFilterType.Image => attachments.Where(attachment => attachment.Message.MessageType == MessageType.Image),
            ChatAttachmentFilterType.Video => attachments.Where(attachment => attachment.Message.MessageType == MessageType.Video),
            ChatAttachmentFilterType.Audio => attachments.Where(attachment => attachment.Message.MessageType == MessageType.Audio),
            ChatAttachmentFilterType.File => attachments.Where(attachment => attachment.Message.MessageType == MessageType.File),
            _ => attachments
        };

        var totalItems = await attachments.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = await attachments
            .OrderByDescending(attachment => attachment.Message.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(attachment => new ChatAttachmentListItemResponse(
                attachment.MessageId,
                attachment.Id,
                attachment.FileUrl,
                attachment.FileName,
                attachment.MimeType,
                attachment.FileSize,
                attachment.Duration,
                attachment.Message.CreatedAt))
            .ToListAsync(cancellationToken);

        return ChatResult<ChatAttachmentListResponse>.Success(new ChatAttachmentListResponse(page, pageSize, totalItems, totalPages, items));
    }

    public async Task<ChatResult<ChatLinkListResponse>> GetLinksAsync(
        GetConversationLinksQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateActiveMemberAsync(query.ConversationId, query.UserId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ChatResult<ChatLinkListResponse>.Failure(validation.Error!.Value, validation.Message!);
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var messages = await dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == query.ConversationId &&
                !message.IsDeleted &&
                message.Content != null &&
                (message.Content.Contains("http://") || message.Content.Contains("https://")))
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => new
            {
                message.Id,
                message.Content,
                Sender = new ChatLinkSenderResponse(message.SenderUser.Id, message.SenderUser.DisplayName),
                message.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var links = messages
            .SelectMany(message => UrlRegex.Matches(message.Content!)
                .Select(match => new ChatLinkItemResponse(message.Id, match.Value, message.Sender, message.CreatedAt)))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
        var totalItems = links.Count;
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        return ChatResult<ChatLinkListResponse>.Success(new ChatLinkListResponse(
            page,
            pageSize,
            totalItems,
            totalPages,
            links.Skip((page - 1) * pageSize).Take(pageSize).ToList()));
    }

    public async Task<ChatResult<ChatMessageSearchResponse>> SearchMessagesAsync(
        SearchConversationMessagesQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateActiveMemberAsync(query.ConversationId, query.UserId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ChatResult<ChatMessageSearchResponse>.Failure(validation.Error!.Value, validation.Message!);
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        if (string.IsNullOrWhiteSpace(query.Keyword))
        {
            return ChatResult<ChatMessageSearchResponse>.Success(new ChatMessageSearchResponse(page, pageSize, 0, 0, []));
        }

        var keyword = RemoveDiacritics(query.Keyword.Trim()).ToLowerInvariant();
        var messages = dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == query.ConversationId &&
                !message.IsDeleted &&
                message.Content != null &&
                AppDbContext.Translate(message.Content.ToLower(), VietnameseDiacritics, VietnameseAscii).Contains(keyword));

        var totalItems = await messages.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = await messages
            .OrderByDescending(message => message.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ChatMessageSearchItemResponse(
                message.Id,
                message.Content,
                new ChatLinkSenderResponse(message.SenderUser.Id, message.SenderUser.DisplayName),
                message.CreatedAt))
            .ToListAsync(cancellationToken);

        return ChatResult<ChatMessageSearchResponse>.Success(new ChatMessageSearchResponse(page, pageSize, totalItems, totalPages, items));
    }

    private async Task PopulateLastMessageAttachmentsAsync(
        List<ChatConversationItemResponse> items,
        CancellationToken cancellationToken)
    {
        var messageIds = items
            .Select(item => item.LastMessage?.Id)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (messageIds.Length == 0)
        {
            return;
        }

        var attachments = await dbContext.MessageAttachments
            .AsNoTracking()
            .Where(attachment => messageIds.Contains(attachment.MessageId))
            .OrderBy(attachment => attachment.Id)
            .Select(attachment => new
            {
                attachment.MessageId,
                Attachment = new ChatMessageAttachmentResponse(
                    attachment.Id,
                    attachment.FileUrl,
                    attachment.FileName,
                    attachment.MimeType,
                    attachment.ThumbnailUrl,
                    attachment.FileSize,
                    attachment.Duration)
            })
            .ToListAsync(cancellationToken);
        var attachmentsByMessage = attachments
            .GroupBy(value => value.MessageId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.Attachment).ToList());

        for (var index = 0; index < items.Count; index++)
        {
            var lastMessage = items[index].LastMessage;
            if (lastMessage is null ||
                lastMessage.MessageType == MessageType.Recall ||
                !attachmentsByMessage.TryGetValue(lastMessage.Id, out var messageAttachments))
            {
                continue;
            }

            items[index] = items[index] with
            {
                LastMessage = lastMessage with { Attachments = messageAttachments }
            };
        }
    }

    private async Task<ChatResult<bool>> ValidateActiveMemberAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var conversationExists = await dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(conversation => conversation.Id == conversationId, cancellationToken);
        if (!conversationExists)
        {
            return ChatResult<bool>.Failure(ChatError.ConversationNotFound, "Khong tim thay cuoc tro chuyen.");
        }

        var isActiveMember = await dbContext.ConversationMembers
            .AsNoTracking()
            .AnyAsync(member =>
                member.ConversationId == conversationId &&
                member.UserId == userId &&
                member.Status == ConversationMemberStatus.Active,
                cancellationToken);
        if (!isActiveMember)
        {
            return ChatResult<bool>.Failure(ChatError.Forbidden, "Ban khong co quyen truy cap cuoc tro chuyen nay.");
        }

        return ChatResult<bool>.Success(true);
    }

    private static bool CanSendToGroup(
        ConversationSendPermission permission,
        ConversationMemberRole role) =>
        permission switch
        {
            ConversationSendPermission.Everyone => true,
            ConversationSendPermission.AdminsAndOwner => role is ConversationMemberRole.Admin or ConversationMemberRole.Owner,
            ConversationSendPermission.OwnerOnly => role == ConversationMemberRole.Owner,
            _ => false
        };

    private Task<List<ChatConversationRecipientState>> BuildRecipientStatesAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.ConversationId == conversationId &&
                member.Status == ConversationMemberStatus.Active)
            .Select(member => new ChatConversationRecipientState(
                member.UserId,
                member.IsMuted,
                member.Conversation.Messages.Count(message =>
                    message.SenderUserId != member.UserId &&
                    (member.LastReadMessageId == null ||
                     message.CreatedAt > member.LastReadMessage!.CreatedAt))))
            .ToListAsync(cancellationToken);

    private static ChatReactionSummaryResponse BuildReactionSummary(
        IReadOnlyCollection<ChatMessageReactionResponse> reactions) =>
        new(
            reactions.Count(reaction => reaction.ReactionType == ReactionType.Like),
            reactions.Count(reaction => reaction.ReactionType == ReactionType.Love),
            reactions.Count(reaction => reaction.ReactionType == ReactionType.Haha),
            reactions.Count(reaction => reaction.ReactionType == ReactionType.Wow),
            reactions.Count(reaction => reaction.ReactionType == ReactionType.Sad),
            reactions.Count(reaction => reaction.ReactionType == ReactionType.Angry),
            reactions.Count);

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder
            .ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormC);
    }
}
