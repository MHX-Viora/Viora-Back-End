using MediatR;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Viora.Application.Posts;
using Viora.Application.Realtime;

namespace Viora.Application.Chat;

public sealed class CreatePrivateConversationHandler(
    IChatConversationRepository repository,
    IValidator<CreatePrivateConversationCommand> validator)
    : IRequestHandler<CreatePrivateConversationCommand, ChatResult<CreatePrivateConversationResponse>>
{
    public async Task<ChatResult<CreatePrivateConversationResponse>> Handle(
        CreatePrivateConversationCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ChatResult<CreatePrivateConversationResponse>.Failure(
                ChatError.Validation,
                validation.Errors[0].ErrorMessage);
        }

        return await repository.CreatePrivateConversationAsync(request, cancellationToken);
    }
}

public sealed class GetChatConversationsHandler(IChatConversationRepository repository)
    : IRequestHandler<GetChatConversationsQuery, ChatConversationListResponse>
{
    public Task<ChatConversationListResponse> Handle(
        GetChatConversationsQuery request,
        CancellationToken cancellationToken) =>
        repository.GetConversationsAsync(
            request with
            {
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 50),
                Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim()
            },
            cancellationToken);
}

public sealed class GetChatConversationMessagesHandler(IChatConversationRepository repository)
    : IRequestHandler<GetChatConversationMessagesQuery, ChatResult<ChatMessageListResponse>>
{
    public Task<ChatResult<ChatMessageListResponse>> Handle(
        GetChatConversationMessagesQuery request,
        CancellationToken cancellationToken) =>
        repository.GetMessagesAsync(
            request with
            {
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 100)
            },
            cancellationToken);
}

public sealed class SendChatMessageHandler(
    IChatConversationRepository repository,
    ChatMessageDeliveryService deliveryService,
    IValidator<SendChatMessageCommand> validator)
    : IRequestHandler<SendChatMessageCommand, ChatResult<SendChatMessageResponse>>
{
    public async Task<ChatResult<SendChatMessageResponse>> Handle(
        SendChatMessageCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ChatResult<SendChatMessageResponse>.Failure(
                ChatError.Validation,
                validation.Errors.First().ErrorMessage);
        }

        var result = await repository.SendMessageAsync(
            request with
            {
                Content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim(),
                Attachments = request.Attachments ?? []
            },
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<SendChatMessageResponse>.Failure(
                result.Error ?? ChatError.Validation,
                result.Message ?? "Khong the gui tin nhan.");
        }

        await deliveryService.PublishAsync(request.SenderUserId, result.Value, cancellationToken);

        return ChatResult<SendChatMessageResponse>.Success(result.Value.Message);
    }
}

public sealed class ForwardChatMessageHandler(
    IChatConversationRepository repository,
    ChatMessageDeliveryService deliveryService,
    IValidator<ForwardChatMessageCommand> validator)
    : IRequestHandler<ForwardChatMessageCommand, ChatResult<ForwardChatMessageResponse>>
{
    public async Task<ChatResult<ForwardChatMessageResponse>> Handle(
        ForwardChatMessageCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ChatResult<ForwardChatMessageResponse>.Failure(ChatError.Validation, validation.Errors[0].ErrorMessage);
        }

        var result = await repository.ForwardMessageAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<ForwardChatMessageResponse>.Failure(
                result.Error ?? ChatError.Validation,
                result.Message ?? "Không thể chuyển tiếp tin nhắn.");
        }

        foreach (var message in result.Value.Messages)
        {
            await deliveryService.PublishAsync(request.UserId, message, cancellationToken);
        }

        return ChatResult<ForwardChatMessageResponse>.Success(new(true));
    }
}

public sealed class ChatMessageDeliveryService(
    IChatConversationRepository repository,
    IRealtimeService realtimeService,
    IPushNotificationSender pushNotificationSender,
    IOnlineUserRegistry onlineUserRegistry,
    Microsoft.Extensions.Logging.ILogger<ChatMessageDeliveryService> logger)
{
    public async Task PublishAsync(
        Guid senderUserId,
        SendChatMessageRepositoryResult result,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in result.Recipients)
        {
            var isMine = recipient.UserId == senderUserId;
            var realtimeMessage = ToRealtimeMessage(result.Message, isMine);

            await realtimeService.SendToUserAsync(recipient.UserId, RealtimeEvents.ReceiveMessage, realtimeMessage, cancellationToken);

            var conversationItem = await repository.GetConversationItemAsync(recipient.UserId, result.Message.ConversationId, cancellationToken);
            if (conversationItem is not null)
            {
                await realtimeService.SendToUserAsync(recipient.UserId, RealtimeEvents.ConversationUpdated, conversationItem, cancellationToken);
            }

            if (isMine)
            {
                await realtimeService.SendToUserAsync(
                    recipient.UserId,
                    RealtimeEvents.MessageDelivered,
                    new MessageDeliveredPayload(result.Message.ConversationId, result.Message.Id, recipient.UserId, result.Message.CreatedAt),
                    cancellationToken);
                continue;
            }

            if (!recipient.IsMuted)
            {
                await realtimeService.SendToUserAsync(
                    recipient.UserId,
                    RealtimeEvents.NewMessageNotification,
                    new NewMessageNotificationPayload(
                        result.Message.ConversationId,
                        conversationItem?.ConversationType ?? default,
                        conversationItem?.Name,
                        conversationItem?.AvatarUrl,
                        result.Message.Sender,
                        new NewMessageNotificationMessagePayload(
                            result.Message.Id,
                            result.Message.Content,
                            result.Message.MessageType,
                            result.Message.Attachments,
                            result.Message.CreatedAt),
                        recipient.UnreadCount,
                        recipient.IsMuted),
                    cancellationToken);
            }

            if (recipient.IsMuted)
            {
                logger.LogInformation(
                    "Chat push skipped because recipient muted conversation. UserId: {UserId}, ConversationId: {ConversationId}, MessageId: {MessageId}.",
                    recipient.UserId,
                    result.Message.ConversationId,
                    result.Message.Id);
                continue;
            }

            if (onlineUserRegistry.IsOnline(recipient.UserId))
            {
                logger.LogInformation(
                    "Chat push skipped because recipient is online. UserId: {UserId}, ConversationId: {ConversationId}, MessageId: {MessageId}.",
                    recipient.UserId,
                    result.Message.ConversationId,
                    result.Message.Id);
                continue;
            }

            logger.LogInformation(
                "Sending chat push to offline recipient. UserId: {UserId}, ConversationId: {ConversationId}, MessageId: {MessageId}.",
                recipient.UserId,
                result.Message.ConversationId,
                result.Message.Id);

            await pushNotificationSender.SendAsync(
                new PushMessage(
                    recipient.UserId,
                    result.Message.Sender.DisplayName,
                    result.Message.Content,
                    new Dictionary<string, string>
                    {
                        ["type"] = "message",
                        ["conversationId"] = result.Message.ConversationId.ToString(),
                        ["messageId"] = result.Message.Id.ToString()
                    }),
                cancellationToken);
        }
    }

    private static ChatRealtimeMessageResponse ToRealtimeMessage(
        SendChatMessageResponse message,
        bool isMine) =>
        new(
            message.Id,
            message.ConversationId,
            message.Sender,
            message.MessageType,
            message.Content,
            message.ReplyMessage,
            message.Attachments,
            [],
            isMine,
            message.IsEdited,
            message.IsDeleted,
            message.CreatedAt);
}

public sealed class MarkConversationReadHandler(
    IChatConversationRepository repository,
    IRealtimeService realtimeService)
    : IRequestHandler<MarkConversationReadCommand, ChatResult<MarkConversationReadResponse>>
{
    public async Task<ChatResult<MarkConversationReadResponse>> Handle(
        MarkConversationReadCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.MarkReadAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<MarkConversationReadResponse>.Failure(
                result.Error ?? ChatError.Validation,
                result.Message ?? "Khong the danh dau da doc.");
        }

        if (result.Value.DidUpdate)
        {
            var payload = new MessagesReadRealtimePayload(
                result.Value.Response.ConversationId,
                request.UserId,
                result.Value.Response.LastReadMessageId,
                result.Value.Response.ReadAt,
                0);

            await realtimeService.SendToUsersAsync(
                result.Value.ConversationMemberIds,
                RealtimeEvents.ConversationRead,
                payload,
                cancellationToken);

            await realtimeService.SendToUsersAsync(
                result.Value.ConversationMemberIds,
                RealtimeEvents.MessagesRead,
                payload,
                cancellationToken);

            var conversationItem = await repository.GetConversationItemAsync(
                request.UserId,
                request.ConversationId,
                cancellationToken);
            if (conversationItem is not null)
            {
                await realtimeService.SendToUserAsync(
                    request.UserId,
                    RealtimeEvents.ConversationUpdated,
                    conversationItem,
                    cancellationToken);
            }
        }

        return ChatResult<MarkConversationReadResponse>.Success(result.Value.Response);
    }
}

public sealed class UploadChatAttachmentsHandler(IMediaStorage mediaStorage)
    : IRequestHandler<UploadChatAttachmentsCommand, ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>>
{
    public async Task<ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>> Handle(
        UploadChatAttachmentsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Files.Count == 0)
        {
            return ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>.Failure(
                ChatError.Validation,
                "Can it nhat mot tep dinh kem.");
        }

        if (request.Files.Count > 10)
        {
            return ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>.Failure(
                ChatError.Validation,
                "Toi da 10 tep dinh kem.");
        }

        var responses = new List<ChatAttachmentUploadResponse>(request.Files.Count);
        foreach (var file in request.Files)
        {
            if (file.Length <= 0)
            {
                return ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>.Failure(
                    ChatError.Validation,
                    "Tep dinh kem khong hop le.");
            }

            var uploaded = await mediaStorage.UploadChatAttachmentAsync(
                request.UserId,
                file,
                cancellationToken);
            responses.Add(new ChatAttachmentUploadResponse(
                uploaded.FileUrl,
                uploaded.FileName,
                uploaded.MimeType,
                uploaded.ThumbnailUrl,
                uploaded.Duration,
                uploaded.FileSize));
        }

        return ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>.Success(responses);
    }
}

public sealed class RecallChatMessageHandler(
    IChatConversationRepository repository,
    IRealtimeService realtimeService)
    : IRequestHandler<RecallChatMessageCommand, ChatResult<RecallChatMessageResponse>>
{
    public async Task<ChatResult<RecallChatMessageResponse>> Handle(
        RecallChatMessageCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.RecallMessageAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<RecallChatMessageResponse>.Failure(
                result.Error ?? ChatError.Validation,
                result.Message ?? "Khong the thu hoi tin nhan.");
        }

        var payload = new MessageDeletedPayload(
            result.Value.Response.ConversationId,
            result.Value.Response.MessageId,
            result.Value.Response.DeletedBy,
            result.Value.Response.DeletedAt);

        await realtimeService.SendToUsersAsync(
            result.Value.ConversationMemberIds,
            RealtimeEvents.MessageDeleted,
            payload,
            cancellationToken);

        foreach (var userId in result.Value.ConversationMemberIds)
        {
            var conversationItem = await repository.GetConversationItemAsync(
                userId,
                result.Value.Response.ConversationId,
                cancellationToken);
            if (conversationItem is not null)
            {
                await realtimeService.SendToUserAsync(
                    userId,
                    RealtimeEvents.ConversationUpdated,
                    conversationItem,
                    cancellationToken);
            }
        }

        return ChatResult<RecallChatMessageResponse>.Success(result.Value.Response);
    }
}

public sealed class SetConversationPinHandler(
    IChatConversationRepository repository,
    IRealtimeService realtimeService)
    : IRequestHandler<SetConversationPinCommand, ChatResult<SetConversationPinResponse>>
{
    public async Task<ChatResult<SetConversationPinResponse>> Handle(
        SetConversationPinCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.SetPinAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<SetConversationPinResponse>.Failure(
                result.Error ?? ChatError.Validation,
                result.Message ?? "Khong the cap nhat trang thai ghim.");
        }

        await realtimeService.SendToUserAsync(
            request.UserId,
            RealtimeEvents.ConversationPinnedChanged,
            new ConversationPinnedChangedPayload(
                result.Value.ConversationId,
                result.Value.IsPinned),
            cancellationToken);

        return ChatResult<SetConversationPinResponse>.Success(result.Value);
    }
}

public sealed class SetConversationMuteHandler(
    IChatConversationRepository repository,
    IRealtimeService realtimeService)
    : IRequestHandler<SetConversationMuteCommand, ChatResult<SetConversationMuteResponse>>
{
    public async Task<ChatResult<SetConversationMuteResponse>> Handle(SetConversationMuteCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.SetMuteAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<SetConversationMuteResponse>.Failure(result.Error ?? ChatError.Validation, result.Message ?? "Khong the cap nhat tat thong bao.");
        }

        await realtimeService.SendToUserAsync(
            request.UserId,
            RealtimeEvents.ConversationMutedChanged,
            new ConversationMutedChangedPayload(result.Value.ConversationId, result.Value.IsMuted),
            cancellationToken);
        return ChatResult<SetConversationMuteResponse>.Success(result.Value);
    }
}

public sealed class SetConversationBlockHandler(
    IChatConversationRepository repository,
    IRealtimeService realtimeService)
    : IRequestHandler<SetConversationBlockCommand, ChatResult<SetConversationBlockResponse>>
{
    public async Task<ChatResult<SetConversationBlockResponse>> Handle(SetConversationBlockCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.SetBlockAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ChatResult<SetConversationBlockResponse>.Failure(result.Error ?? ChatError.Validation, result.Message ?? "Khong the cap nhat chan cuoc tro chuyen.");
        }

        await realtimeService.SendToUserAsync(
            request.UserId,
            RealtimeEvents.ConversationBlockedChanged,
            new ConversationBlockedChangedPayload(result.Value.ConversationId, result.Value.IsBlocked),
            cancellationToken);
        return ChatResult<SetConversationBlockResponse>.Success(result.Value);
    }
}

public sealed class GetConversationInfoHandler(IChatConversationRepository repository)
    : IRequestHandler<GetConversationInfoQuery, ChatResult<ChatConversationInfoResponse>>
{
    public Task<ChatResult<ChatConversationInfoResponse>> Handle(GetConversationInfoQuery request, CancellationToken cancellationToken) =>
        repository.GetInfoAsync(request, cancellationToken);
}

public sealed class GetConversationAttachmentsHandler(IChatConversationRepository repository)
    : IRequestHandler<GetConversationAttachmentsQuery, ChatResult<ChatAttachmentListResponse>>
{
    public Task<ChatResult<ChatAttachmentListResponse>> Handle(GetConversationAttachmentsQuery request, CancellationToken cancellationToken) =>
        repository.GetAttachmentsAsync(request with
        {
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Clamp(request.PageSize, 1, 100)
        }, cancellationToken);
}

public sealed class GetConversationLinksHandler(IChatConversationRepository repository)
    : IRequestHandler<GetConversationLinksQuery, ChatResult<ChatLinkListResponse>>
{
    public Task<ChatResult<ChatLinkListResponse>> Handle(GetConversationLinksQuery request, CancellationToken cancellationToken) =>
        repository.GetLinksAsync(request with
        {
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Clamp(request.PageSize, 1, 100)
        }, cancellationToken);
}

public sealed class SearchConversationMessagesHandler(IChatConversationRepository repository)
    : IRequestHandler<SearchConversationMessagesQuery, ChatResult<ChatMessageSearchResponse>>
{
    public Task<ChatResult<ChatMessageSearchResponse>> Handle(SearchConversationMessagesQuery request, CancellationToken cancellationToken) =>
        repository.SearchMessagesAsync(request with
        {
            Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Clamp(request.PageSize, 1, 100)
        }, cancellationToken);
}
