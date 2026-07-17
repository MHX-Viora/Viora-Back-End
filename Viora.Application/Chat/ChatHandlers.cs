using MediatR;
using FluentValidation;
using Viora.Application.Realtime;

namespace Viora.Application.Chat;

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
    IRealtimeService realtimeService,
    IPushNotificationSender pushNotificationSender,
    IOnlineUserRegistry onlineUserRegistry,
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

        await realtimeService.SendToUsersAsync(
            result.Value.ConversationMemberIds,
            RealtimeEvents.ReceiveMessage,
            result.Value.Message,
            cancellationToken);

        foreach (var recipientId in result.Value.ConversationMemberIds
                     .Where(id => id != request.SenderUserId && !onlineUserRegistry.IsOnline(id)))
        {
            await pushNotificationSender.SendAsync(
                new PushMessage(
                    recipientId,
                    result.Value.Message.Sender.DisplayName,
                    result.Value.Message.Content,
                    new Dictionary<string, string>
                    {
                        ["type"] = "message",
                        ["conversationId"] = request.ConversationId.ToString(),
                        ["messageId"] = result.Value.Message.Id.ToString()
                    }),
                cancellationToken);
        }

        return ChatResult<SendChatMessageResponse>.Success(result.Value.Message);
    }
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
            await realtimeService.SendToUsersAsync(
                result.Value.ConversationMemberIds,
                RealtimeEvents.MessagesRead,
                new MessagesReadRealtimePayload(
                    result.Value.Response.ConversationId,
                    request.UserId,
                    result.Value.Response.LastReadMessageId,
                    result.Value.Response.ReadAt),
                cancellationToken);
        }

        return ChatResult<MarkConversationReadResponse>.Success(result.Value.Response);
    }
}
