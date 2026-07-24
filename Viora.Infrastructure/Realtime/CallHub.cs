using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Viora.Application.Calls;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Realtime;

[Authorize]
public sealed class CallHub(ICallRepository repository, ILogger<CallHub> logger) : Hub
{
    public Task AcceptCall(Guid callId) => ForwardCallEventAsync(callId, "CallAccepted", null);
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
        if (!CanForward(eventName, routing.Status, routing.CallerId == userId))
        {
            logger.LogWarning(
                "Rejected call signal {EventName}. CallId: {CallId}, UserId: {UserId}, Status: {Status}.",
                eventName,
                callId,
                userId,
                routing.Status);
            throw new HubException("Call state does not allow this action.");
        }

        object payload = signal is null
            ? new { routing.CallId, routing.ConversationId, FromUserId = userId }
            : new CallSignalPayload(routing.CallId, routing.ConversationId, userId, signal);
        await Clients.User(routing.OtherUserId.ToString()).SendAsync(eventName, payload, Context.ConnectionAborted);
        logger.LogInformation(
            "Forwarded call signal {EventName}. CallId: {CallId}, FromUserId: {UserId}, ToUserId: {OtherUserId}.",
            eventName,
            callId,
            userId,
            routing.OtherUserId);
    }

    private static bool CanForward(string eventName, CallStatus status, bool isCaller) => eventName switch
    {
        "CallAccepted" => status == CallStatus.Accepted && !isCaller,
        "ReceiveOffer" => status == CallStatus.Accepted && isCaller,
        "ReceiveAnswer" => status == CallStatus.Accepted && !isCaller,
        "ReceiveIceCandidate" or "ReconnectCall" => status == CallStatus.Accepted,
        _ => false
    };

    private bool TryGetUserId(out Guid userId)
    {
        var value = Context.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
