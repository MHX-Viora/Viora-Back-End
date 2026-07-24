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
