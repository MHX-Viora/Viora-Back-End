using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Calls;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Calls;

public sealed class CallDeliveryServiceTests
{
    [Fact]
    public async Task Incoming_call_push_is_always_sent_alongside_realtime_delivery()
    {
        var push = new RecordingPushSender();
        var service = new CallDeliveryService(
            new RecordingRealtimeService(),
            new NoOpCallHistoryRepository(),
            push,
            NullLogger<CallDeliveryService>.Instance);
        var now = DateTime.UtcNow;
        var call = new CallSessionResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CallParticipantResponse(Guid.NewGuid(), "Nguyễn Văn A", "https://example.test/avatar.jpg"),
            new CallParticipantResponse(Guid.NewGuid(), "Người nhận", null),
            CallType.Video,
            CallStatus.Calling,
            now,
            null,
            null,
            null,
            now,
            now);

        await service.PublishIncomingAsync(call, CancellationToken.None);

        var message = Assert.Single(push.Messages);
        Assert.Equal(call.Receiver.Id, message.UserId);
        Assert.Equal("IncomingCall", message.Data["type"]);
        Assert.Equal(call.Id.ToString(), message.Data["callId"]);
        Assert.Equal(((short)CallType.Video).ToString(), message.Data["callType"]);
        Assert.Equal(call.Caller.DisplayName, message.Data["callerDisplayName"]);
        Assert.Equal(call.Caller.AvatarUrl, message.Data["callerAvatarUrl"]);
    }

    [Fact]
    public async Task Incoming_call_push_is_attempted_when_realtime_delivery_fails()
    {
        var push = new RecordingPushSender();
        var service = new CallDeliveryService(
            new FailingRealtimeService(),
            new NoOpCallHistoryRepository(),
            push,
            NullLogger<CallDeliveryService>.Instance);
        var now = DateTime.UtcNow;
        var call = new CallSessionResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CallParticipantResponse(Guid.NewGuid(), "Người gọi", null),
            new CallParticipantResponse(Guid.NewGuid(), "Người nhận", null),
            CallType.Audio,
            CallStatus.Calling,
            now,
            null,
            null,
            null,
            now,
            now);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishIncomingAsync(call, CancellationToken.None));

        Assert.Single(push.Messages);
    }

    private sealed class RecordingPushSender : IPushNotificationSender
    {
        public List<PushMessage> Messages { get; } = [];

        public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpCallHistoryRepository : ICallHistoryMessageRepository
    {
        public Task<CallHistoryMessage?> CreateAsync(
            CallSessionResponse call,
            CancellationToken cancellationToken) => Task.FromResult<CallHistoryMessage?>(null);
    }

    private sealed class RecordingRealtimeService : IRealtimeService
    {
        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingRealtimeService : IRealtimeService
    {
        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.FromException(new InvalidOperationException("Realtime unavailable"));
        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
