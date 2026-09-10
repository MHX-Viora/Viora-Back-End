using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Calls;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Calls;

public sealed class CallDeliveryServiceTests
{
    [Fact]
    public async Task AcceptedCallNotifiesCallerAndOtherReceiverConnections()
    {
        var realtime = new RecordingRealtimeService();
        var delivery = new CallDeliveryService(
            realtime,
            new EmptyHistoryRepository(),
            new NoOpPushSender(),
            NullLogger<CallDeliveryService>.Instance);
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var call = CreateCall(callerId, receiverId);

        await delivery.PublishAcceptedAsync(call, "winner-connection", CancellationToken.None);

        Assert.Contains(realtime.Events, item =>
            item.UserId == callerId && item.EventName == "CallAccepted");
        var answeredElsewhere = Assert.Single(realtime.Events, item =>
            item.UserId == receiverId && item.EventName == "CallAnsweredElsewhere");
        var payload = Assert.IsType<CallAnsweredElsewherePayload>(answeredElsewhere.Payload);
        Assert.Equal("winner-connection", payload.AcceptedConnectionId);
    }

    private static CallSessionResponse CreateCall(Guid callerId, Guid receiverId) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        new CallParticipantResponse(callerId, "Caller", null),
        new CallParticipantResponse(receiverId, "Receiver", null),
        CallType.Audio,
        CallStatus.Accepted,
        DateTime.UtcNow,
        DateTime.UtcNow,
        null,
        null,
        DateTime.UtcNow,
        DateTime.UtcNow);

    private sealed class RecordingRealtimeService : IRealtimeService
    {
        public List<(Guid UserId, string EventName, object Payload)> Events { get; } = [];

        public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken)
        {
            Events.Add((userId, eventName, payload));
            return Task.CompletedTask;
        }

        public Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddUsersToGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveUsersFromGroupAsync(IEnumerable<Guid> userIds, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyHistoryRepository : ICallHistoryMessageRepository
    {
        public Task<CallHistoryMessage?> CreateAsync(CallSessionResponse call, CancellationToken cancellationToken) =>
            Task.FromResult<CallHistoryMessage?>(null);
    }

    private sealed class NoOpPushSender : IPushNotificationSender
    {
        public Task SendAsync(PushMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
