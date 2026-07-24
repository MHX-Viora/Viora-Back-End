using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Calls;

public sealed record CreateCallCommand(Guid CallerId, Guid ConversationId, CallType CallType) : IRequest<CallResult<CreateCallResponse>>;
public sealed record AcceptCallCommand(Guid UserId, Guid CallId) : IRequest<CallResult<CallSessionResponse>>;
public sealed record RejectCallCommand(Guid UserId, Guid CallId) : IRequest<CallResult<CallSessionResponse>>;
public sealed record CancelCallCommand(Guid UserId, Guid CallId) : IRequest<CallResult<CallSessionResponse>>;
public sealed record EndCallCommand(Guid UserId, Guid CallId) : IRequest<CallResult<CallSessionResponse>>;
public sealed record MarkMissedCallCommand(Guid CallId) : IRequest<CallResult<CallSessionResponse>>;
public sealed record GetCallHistoryQuery(Guid UserId, int Page, int PageSize) : IRequest<CallHistoryResponse>;
public sealed record GetCallByIdQuery(Guid UserId, Guid CallId) : IRequest<CallResult<CallSessionResponse>>;

public sealed record CreateCallRequest(Guid ConversationId, CallType CallType = CallType.Audio);
public sealed record CreateCallResponse(Guid CallId);

public sealed record CallHistoryResponse(int Page, int PageSize, int TotalItems, int TotalPages, IReadOnlyList<CallSessionResponse> Items);

public sealed record CallSessionResponse(
    Guid Id,
    Guid ConversationId,
    CallParticipantResponse Caller,
    CallParticipantResponse Receiver,
    CallType CallType,
    CallStatus Status,
    DateTime StartedAt,
    DateTime? AnsweredAt,
    DateTime? EndedAt,
    int? Duration,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CallParticipantResponse(Guid Id, string DisplayName, string? AvatarUrl);
public sealed record IceServersResponse(IReadOnlyList<IceServerResponse> IceServers);
public sealed record IceServerResponse(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);
public sealed record IncomingCallPayload(Guid CallId, Guid ConversationId, CallParticipantResponse Caller, CallType CallType);
public sealed record CallSignalPayload(Guid CallId, Guid ConversationId, Guid FromUserId, object Signal);
public sealed record CallEndedPayload(Guid CallId, Guid ConversationId, CallStatus Status, int? Duration);
public sealed record CallHistoryMessage(
    Guid Id,
    Guid ConversationId,
    CallParticipantResponse Sender,
    bool SenderIsVerified,
    string Content,
    DateTime CreatedAt);

public static class CallHistoryMessages
{
    public static string Format(CallType callType, CallStatus status, int? duration)
    {
        var label = callType == CallType.Video ? "Cuộc gọi video" : "Cuộc gọi thoại";
        return status switch
        {
            CallStatus.Ended => $"{label} • {FormatDuration(duration ?? 0)}",
            CallStatus.Rejected => $"{label} bị từ chối",
            CallStatus.Cancelled => $"{label} đã hủy",
            CallStatus.Missed => $"{label} nhỡ",
            _ => label
        };
    }

    private static string FormatDuration(int duration)
    {
        var safeDuration = Math.Max(0, duration);
        var value = TimeSpan.FromSeconds(safeDuration);
        return safeDuration >= 3600
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }
}

public enum CallError
{
    NotFound,
    ConversationNotFound,
    ConversationDissolved,
    Forbidden,
    Blocked,
    Busy,
    InvalidState,
    Validation
}

public sealed record CallResult<T>(bool IsSuccess, T? Value, CallError? Error, string? Message)
{
    public static CallResult<T> Success(T value) => new(true, value, null, null);
    public static CallResult<T> Failure(CallError error, string message) => new(false, default, error, message);
}

public interface ICallRepository
{
    Task<CallResult<CallSessionResponse>> CreateCallAsync(CreateCallCommand command, CancellationToken cancellationToken);
    Task<CallResult<CallSessionResponse>> AcceptCallAsync(AcceptCallCommand command, CancellationToken cancellationToken);
    Task<CallResult<CallSessionResponse>> RejectCallAsync(RejectCallCommand command, CancellationToken cancellationToken);
    Task<CallResult<CallSessionResponse>> CancelCallAsync(CancelCallCommand command, CancellationToken cancellationToken);
    Task<CallResult<CallSessionResponse>> EndCallAsync(EndCallCommand command, CancellationToken cancellationToken);
    Task<CallResult<CallSessionResponse>> MarkMissedAsync(Guid callId, CancellationToken cancellationToken);
    Task<CallHistoryResponse> GetHistoryAsync(GetCallHistoryQuery query, CancellationToken cancellationToken);
    Task<CallResult<CallSessionResponse>> GetByIdAsync(Guid userId, Guid callId, CancellationToken cancellationToken);
    Task<CallParticipantRouting?> GetParticipantRoutingAsync(Guid userId, Guid callId, CancellationToken cancellationToken);
}

public interface IIceServerProvider
{
    IceServersResponse Get();
}

public interface ICallHistoryMessageRepository
{
    Task<CallHistoryMessage?> CreateAsync(
        CallSessionResponse call,
        CancellationToken cancellationToken);
}

public sealed record CallParticipantRouting(Guid CallId, Guid ConversationId, Guid CallerId, Guid ReceiverId, Guid OtherUserId, CallStatus Status);

public sealed class CreateCallValidator : AbstractValidator<CreateCallCommand>
{
    public CreateCallValidator()
    {
        RuleFor(command => command.CallerId).NotEmpty();
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.CallType).IsInEnum();
    }
}
