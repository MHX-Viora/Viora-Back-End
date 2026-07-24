using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Viora.Application.Chat;
using Viora.Application.Realtime;

namespace Viora.Application.Calls;

public sealed class CreateCallHandler(
    ICallRepository repository,
    CallDeliveryService deliveryService,
    IValidator<CreateCallCommand> validator)
    : IRequestHandler<CreateCallCommand, CallResult<CreateCallResponse>>
{
    public async Task<CallResult<CreateCallResponse>> Handle(CreateCallCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CallResult<CreateCallResponse>.Failure(CallError.Validation, validation.Errors[0].ErrorMessage);
        }

        var result = await repository.CreateCallAsync(request, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return CallResult<CreateCallResponse>.Failure(result.Error ?? CallError.Validation, result.Message ?? "Không thể tạo cuộc gọi.");
        }

        await deliveryService.PublishIncomingAsync(result.Value, cancellationToken);
        return CallResult<CreateCallResponse>.Success(new CreateCallResponse(result.Value.Id));
    }
}

public sealed class AcceptCallHandler(ICallRepository repository, CallDeliveryService deliveryService)
    : IRequestHandler<AcceptCallCommand, CallResult<CallSessionResponse>>
{
    public async Task<CallResult<CallSessionResponse>> Handle(AcceptCallCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.AcceptCallAsync(request, cancellationToken);
        if (result.IsSuccess && result.Value is not null) await deliveryService.PublishAcceptedAsync(result.Value, cancellationToken);
        return result;
    }
}

public sealed class RejectCallHandler(ICallRepository repository, CallDeliveryService deliveryService)
    : IRequestHandler<RejectCallCommand, CallResult<CallSessionResponse>>
{
    public async Task<CallResult<CallSessionResponse>> Handle(RejectCallCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.RejectCallAsync(request, cancellationToken);
        if (result.IsSuccess && result.Value is not null) await deliveryService.PublishEndedAsync(result.Value, "CallRejected", cancellationToken);
        return result;
    }
}

public sealed class CancelCallHandler(ICallRepository repository, CallDeliveryService deliveryService)
    : IRequestHandler<CancelCallCommand, CallResult<CallSessionResponse>>
{
    public async Task<CallResult<CallSessionResponse>> Handle(CancelCallCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.CancelCallAsync(request, cancellationToken);
        if (result.IsSuccess && result.Value is not null) await deliveryService.PublishEndedAsync(result.Value, "CallCancelled", cancellationToken);
        return result;
    }
}

public sealed class EndCallHandler(ICallRepository repository, CallDeliveryService deliveryService)
    : IRequestHandler<EndCallCommand, CallResult<CallSessionResponse>>
{
    public async Task<CallResult<CallSessionResponse>> Handle(EndCallCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.EndCallAsync(request, cancellationToken);
        if (result.IsSuccess && result.Value is not null) await deliveryService.PublishEndedAsync(result.Value, "CallEnded", cancellationToken);
        return result;
    }
}

public sealed class MarkMissedCallHandler(ICallRepository repository, CallDeliveryService deliveryService)
    : IRequestHandler<MarkMissedCallCommand, CallResult<CallSessionResponse>>
{
    public async Task<CallResult<CallSessionResponse>> Handle(MarkMissedCallCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.MarkMissedAsync(request.CallId, cancellationToken);
        if (result.IsSuccess && result.Value is not null) await deliveryService.PublishMissedAsync(result.Value, cancellationToken);
        return result;
    }
}

public sealed class GetCallHistoryHandler(ICallRepository repository) : IRequestHandler<GetCallHistoryQuery, CallHistoryResponse>
{
    public Task<CallHistoryResponse> Handle(GetCallHistoryQuery request, CancellationToken cancellationToken) =>
        repository.GetHistoryAsync(request with { Page = Math.Max(request.Page, 1), PageSize = Math.Clamp(request.PageSize, 1, 50) }, cancellationToken);
}

public sealed class GetCallByIdHandler(ICallRepository repository) : IRequestHandler<GetCallByIdQuery, CallResult<CallSessionResponse>>
{
    public Task<CallResult<CallSessionResponse>> Handle(GetCallByIdQuery request, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(request.UserId, request.CallId, cancellationToken);
}

public sealed class CallDeliveryService(
    IRealtimeService realtimeService,
    ICallHistoryMessageRepository historyMessageRepository,
    IPushNotificationSender pushNotificationSender,
    IOnlineUserRegistry onlineUserRegistry,
    ILogger<CallDeliveryService> logger)
{
    public async Task PublishIncomingAsync(CallSessionResponse call, CancellationToken cancellationToken)
    {
        var payload = new IncomingCallPayload(call.Id, call.ConversationId, call.Caller, call.CallType);
        await realtimeService.SendToUserAsync(call.Receiver.Id, "IncomingCall", payload, cancellationToken);
        logger.LogInformation("Call Started. CallId: {CallId}, CallerId: {CallerId}, ReceiverId: {ReceiverId}.", call.Id, call.Caller.Id, call.Receiver.Id);

        if (!onlineUserRegistry.IsOnline(call.Receiver.Id))
        {
            await pushNotificationSender.SendAsync(new PushMessage(
                call.Receiver.Id,
                call.Caller.DisplayName,
                "Đang gọi cho bạn...",
                new Dictionary<string, string>
                {
                    ["type"] = "IncomingCall",
                    ["callId"] = call.Id.ToString(),
                    ["callType"] = ((short)call.CallType).ToString(),
                    ["conversationId"] = call.ConversationId.ToString(),
                    ["callerId"] = call.Caller.Id.ToString(),
                    ["callerDisplayName"] = call.Caller.DisplayName,
                    ["callerAvatarUrl"] = call.Caller.AvatarUrl ?? string.Empty
                }), cancellationToken);
        }
    }

    public Task PublishAcceptedAsync(CallSessionResponse call, CancellationToken cancellationToken)
    {
        logger.LogInformation("Call Accepted. CallId: {CallId}.", call.Id);
        return realtimeService.SendToUserAsync(call.Caller.Id, "CallAccepted", call, cancellationToken);
    }

    public async Task PublishEndedAsync(CallSessionResponse call, string eventName, CancellationToken cancellationToken)
    {
        logger.LogInformation("Call {EventName}. CallId: {CallId}, Duration: {Duration}.", eventName, call.Id, call.Duration);
        var payload = new CallEndedPayload(call.Id, call.ConversationId, call.Status, call.Duration);
        await realtimeService.SendToUsersAsync([call.Caller.Id, call.Receiver.Id], eventName, payload, cancellationToken);
        await PublishHistoryMessageAsync(call, cancellationToken);
    }

    public async Task PublishMissedAsync(CallSessionResponse call, CancellationToken cancellationToken)
    {
        logger.LogInformation("Call Missed. CallId: {CallId}.", call.Id);
        var payload = new CallEndedPayload(call.Id, call.ConversationId, call.Status, call.Duration);
        await realtimeService.SendToUserAsync(call.Caller.Id, "CallTimeout", payload, cancellationToken);
        await realtimeService.SendToUserAsync(call.Receiver.Id, "CallMissed", payload, cancellationToken);
        await PublishHistoryMessageAsync(call, cancellationToken);
        await pushNotificationSender.SendAsync(new PushMessage(
            call.Receiver.Id,
            "Cuộc gọi nhỡ",
            call.Caller.DisplayName,
            new Dictionary<string, string>
            {
                ["type"] = "MissedCall",
                ["callId"] = call.Id.ToString(),
                ["conversationId"] = call.ConversationId.ToString()
            }), cancellationToken);
    }

    private async Task PublishHistoryMessageAsync(
        CallSessionResponse call,
        CancellationToken cancellationToken)
    {
        var message = await historyMessageRepository.CreateAsync(call, cancellationToken);
        if (message is null) return;

        var sender = new ChatMessageSenderResponse(
            message.Sender.Id,
            message.Sender.DisplayName,
            message.Sender.AvatarUrl,
            message.SenderIsVerified);
        foreach (var userId in new[] { call.Caller.Id, call.Receiver.Id })
        {
            var payload = GroupChatRealtimeMessages.CreateSystemMessage(
                message.Id,
                message.ConversationId,
                sender,
                message.Content,
                message.CreatedAt,
                userId == message.Sender.Id);
            await realtimeService.SendToUserAsync(
                userId,
                RealtimeEvents.ReceiveMessage,
                payload,
                cancellationToken);
        }
    }
}
