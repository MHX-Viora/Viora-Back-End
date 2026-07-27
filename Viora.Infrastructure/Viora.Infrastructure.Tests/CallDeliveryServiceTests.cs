using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Calls;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CallDeliveryServiceTests
{
    [Theory]
    [InlineData("CallRejected")]
    [InlineData("CallCancelled")]
    [InlineData("CallEnded")]
    public async Task PublishEndedAsync_sends_lifecycle_push_to_both_participants(string eventName)
    {
        var push = new FakePushNotificationSender();
        var service = new CallDeliveryService(
            new NoOpRealtimeService(),
            new NoOpCallHistoryMessageRepository(),
            push,
            new OfflineUserRegistry(),
            NullLogger<CallDeliveryService>.Instance);
        var call = CreateCall();

        await service.PublishEndedAsync(call, eventName, CancellationToken.None);

        Assert.Equal(2, push.Messages.Count);
        Assert.Equal(
            [call.Caller.Id, call.Receiver.Id],
            push.Messages.Select(message => message.UserId));
        Assert.All(push.Messages, message =>
        {
            Assert.Equal(eventName, message.Data["type"]);
            Assert.Equal(call.Id.ToString(), message.Data["callId"]);
            Assert.Equal(call.ConversationId.ToString(), message.Data["conversationId"]);
        });
    }

    private static CallSessionResponse CreateCall() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        new CallParticipantResponse(Guid.NewGuid(), "Caller", null),
        new CallParticipantResponse(Guid.NewGuid(), "Receiver", null),
        CallType.Audio,
        CallStatus.Rejected,
        DateTime.UtcNow,
        null,
        DateTime.UtcNow,
        null,
        DateTime.UtcNow,
        DateTime.UtcNow);

    private sealed class FakePushNotificationSender : IPushNotificationSender
    {
        public List<PushMessage> Messages { get; } = [];

        public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpRealtimeService : IRealtimeService
    {
        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpCallHistoryMessageRepository : ICallHistoryMessageRepository
    {
        public Task<CallHistoryMessage?> CreateAsync(CallSessionResponse call, CancellationToken cancellationToken) =>
            Task.FromResult<CallHistoryMessage?>(null);
    }

    private sealed class OfflineUserRegistry : IOnlineUserRegistry
    {
        public bool IsOnline(Guid userId) => false;
    }
}
