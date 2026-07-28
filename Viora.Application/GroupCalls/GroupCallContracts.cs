using Viora.Domain.Entities;

namespace Viora.Application.GroupCalls;

public sealed record LiveKitJoinCredentials(string LiveKitUrl, string Token);

public interface ILiveKitTokenIssuer
{
    LiveKitJoinCredentials Issue(
        Guid callId,
        Guid userId,
        string displayName,
        GroupCallType callType);
}

public sealed class LiveKitConfigurationException(string message) : Exception(message);

public sealed record StartGroupCallRequest(Guid ConversationId, GroupCallType CallType);
public sealed record GroupCallParticipantResponse(Guid Id, string DisplayName, string? AvatarUrl);
public sealed record GroupCallResponse(
    Guid Id,
    Guid ConversationId,
    GroupCallParticipantResponse StartedBy,
    GroupCallType CallType,
    GroupCallStatus Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    int? Duration);
public sealed record GroupCallJoinResponse(GroupCallResponse Call, string LiveKitUrl, string Token);

public enum GroupCallError
{
    NotFound,
    Forbidden,
    InvalidConversation,
    TooManyParticipants,
    InvalidState,
    Configuration
}

public sealed record GroupCallResult<T>(bool IsSuccess, T? Value, GroupCallError? Error, string? Message)
{
    public static GroupCallResult<T> Success(T value) => new(true, value, null, null);
    public static GroupCallResult<T> Failure(GroupCallError error, string message) =>
        new(false, default, error, message);
}

public interface IGroupCallService
{
    Task<GroupCallResult<GroupCallJoinResponse>> StartAsync(Guid userId, StartGroupCallRequest request, CancellationToken cancellationToken);
    Task<GroupCallResult<GroupCallJoinResponse>> JoinAsync(Guid userId, Guid callId, CancellationToken cancellationToken);
    Task<GroupCallResult<GroupCallResponse>> GetAsync(Guid userId, Guid callId, CancellationToken cancellationToken);
    Task<GroupCallResult<GroupCallResponse>> GetActiveAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
    Task<GroupCallResult<GroupCallResponse>> EndAsync(Guid userId, Guid callId, CancellationToken cancellationToken);
}
