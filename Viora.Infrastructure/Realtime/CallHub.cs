using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Viora.Application.Calls;

namespace Viora.Infrastructure.Realtime;

[Authorize]
public sealed class CallHub(ICallRepository repository) : Hub
{
    public Task CallUser(Guid callId) => ForwardCallEventAsync(callId, "IncomingCall", null);
    public Task AcceptCall(Guid callId) => ForwardCallEventAsync(callId, "CallAccepted", null);
    public Task RejectCall(Guid callId) => ForwardCallEventAsync(callId, "CallRejected", null);
    public Task CancelCall(Guid callId) => ForwardCallEventAsync(callId, "CallCancelled", null);
    public Task EndCall(Guid callId) => ForwardCallEventAsync(callId, "CallEnded", null);
    public Task ReconnectCall(Guid callId) => ForwardCallEventAsync(callId, "ReconnectCall", null);
    public Task Offer(Guid callId, object offer) => ForwardCallEventAsync(callId, "ReceiveOffer", offer);
    public Task Answer(Guid callId, object answer) => ForwardCallEventAsync(callId, "ReceiveAnswer", answer);
    public Task IceCandidate(Guid callId, object candidate) => ForwardCallEventAsync(callId, "ReceiveIceCandidate", candidate);

    private async Task ForwardCallEventAsync(Guid callId, string eventName, object? signal)
    {
        if (!TryGetUserId(out var userId))
        {
            throw new HubException("Unauthorized.");
        }

        var routing = await repository.GetParticipantRoutingAsync(userId, callId, Context.ConnectionAborted);
        if (routing is null)
        {
            throw new HubException("Call is not accessible.");
        }

        object payload = signal is null
            ? new { routing.CallId, routing.ConversationId, FromUserId = userId }
            : new CallSignalPayload(routing.CallId, routing.ConversationId, userId, signal);
        await Clients.User(routing.OtherUserId.ToString()).SendAsync(eventName, payload, Context.ConnectionAborted);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
