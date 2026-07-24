using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Chat;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ChatMessageDeliveryServiceTests
{
    [Fact]
    public async Task PublishAsync_offline_recipient_receives_chat_push_payload()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var push = new FakePushNotificationSender();
        var service = new ChatMessageDeliveryService(
            new FakeChatConversationRepository(),
            new FakeRealtimeService(),
            push,
            new FakeOnlineUserRegistry(),
            NullLogger<ChatMessageDeliveryService>.Instance);

        await service.PublishAsync(senderId, CreateResult(conversationId, messageId, senderId, recipientId, isMuted: false), CancellationToken.None);

        var message = Assert.Single(push.Messages);
        Assert.Equal(recipientId, message.UserId);
        Assert.Equal("chat", message.Data["type"]);
        Assert.Equal("message", message.Data["eventType"]);
        Assert.Equal(conversationId.ToString(), message.Data["conversationId"]);
        Assert.Equal(messageId.ToString(), message.Data["messageId"]);
        Assert.Equal("Sender", message.Title);
        Assert.Equal("Hello", message.Body);
    }

    [Fact]
    public async Task PublishAsync_online_recipient_still_receives_chat_push_payload()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var push = new FakePushNotificationSender();
        var service = new ChatMessageDeliveryService(
            new FakeChatConversationRepository(),
            new FakeRealtimeService(),
            push,
            new FakeOnlineUserRegistry(recipientId),
            NullLogger<ChatMessageDeliveryService>.Instance);

        await service.PublishAsync(
            senderId,
            CreateResult(Guid.NewGuid(), Guid.NewGuid(), senderId, recipientId, isMuted: false),
            CancellationToken.None);

        Assert.Single(push.Messages);
    }

    [Fact]
    public async Task PublishAsync_muted_recipient_does_not_receive_push()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var push = new FakePushNotificationSender();
        var service = new ChatMessageDeliveryService(
            new FakeChatConversationRepository(),
            new FakeRealtimeService(),
            push,
            new FakeOnlineUserRegistry(),
            NullLogger<ChatMessageDeliveryService>.Instance);

        await service.PublishAsync(senderId, CreateResult(Guid.NewGuid(), Guid.NewGuid(), senderId, recipientId, isMuted: true), CancellationToken.None);

        Assert.Empty(push.Messages);
    }

    private static SendChatMessageRepositoryResult CreateResult(
        Guid conversationId,
        Guid messageId,
        Guid senderId,
        Guid recipientId,
        bool isMuted) =>
        new(
            new SendChatMessageResponse(
                messageId,
                conversationId,
                new ChatMessageSenderResponse(senderId, "Sender", null, false),
                MessageType.Text,
                "Hello",
                null,
                [],
                true,
                false,
                false,
                DateTime.UtcNow),
            [
                new ChatConversationRecipientState(senderId, false, 0),
                new ChatConversationRecipientState(recipientId, isMuted, 3)
            ]);

    private sealed class FakeRealtimeService : IRealtimeService
    {
        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePushNotificationSender : IPushNotificationSender
    {
        public List<PushMessage> Messages { get; } = [];
        public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOnlineUserRegistry(params Guid[] onlineUserIds) : IOnlineUserRegistry
    {
        public bool IsOnline(Guid userId) => onlineUserIds.Contains(userId);
    }

    private sealed class FakeChatConversationRepository : IChatConversationRepository
    {
        public Task<ChatResult<CreatePrivateConversationResponse>> CreatePrivateConversationAsync(CreatePrivateConversationCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatConversationListResponse> GetConversationsAsync(GetChatConversationsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatUnreadSummaryResponse> GetUnreadSummaryAsync(GetChatUnreadSummaryQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<ChatMessageListResponse>> GetMessagesAsync(GetChatConversationMessagesQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<SendChatMessageRepositoryResult>> SendMessageAsync(SendChatMessageCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<MarkConversationReadRepositoryResult>> MarkReadAsync(MarkConversationReadCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatConversationItemResponse?> GetConversationItemAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<ChatConversationItemResponse?>(null);
        public Task<ChatResult<RecallChatMessageRepositoryResult>> RecallMessageAsync(RecallChatMessageCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<ForwardChatMessageRepositoryResult>> ForwardMessageAsync(ForwardChatMessageCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<SetConversationPinResponse>> SetPinAsync(SetConversationPinCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<SetConversationMuteResponse>> SetMuteAsync(SetConversationMuteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<SetConversationBlockResponse>> SetBlockAsync(SetConversationBlockCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<ChatConversationInfoResponse>> GetInfoAsync(GetConversationInfoQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<ChatAttachmentListResponse>> GetAttachmentsAsync(GetConversationAttachmentsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<ChatLinkListResponse>> GetLinksAsync(GetConversationLinksQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChatResult<ChatMessageSearchResponse>> SearchMessagesAsync(SearchConversationMessagesQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
