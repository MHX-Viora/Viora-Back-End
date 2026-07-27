using Viora.Domain.Entities;

namespace Viora.Application.Mentions;

public sealed record MentionResponse(Guid UserId, string DisplayName);
public sealed record MentionSearchResponse(Guid Id, string DisplayName, string? AvatarUrl, bool IsVerified);
public sealed record MentionNotificationReference(Guid Id, NotificationReferenceType Type);

public interface IMentionService
{
    Task<IReadOnlyList<MentionResponse>> CreateAsync(
        Guid mentionedByUserId,
        Guid targetId,
        MentionTargetType targetType,
        IReadOnlyList<Guid>? mentionedUserIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MentionSearchResponse>> SearchAsync(
        Guid currentUserId,
        string? keyword,
        CancellationToken cancellationToken);
}

public interface IMentionRepository
{
    Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> GetEligibleUsersAsync(
        Guid mentionedByUserId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);
    Task AddRangeAsync(IReadOnlyList<Mention> mentions, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<MentionNotificationReference?> GetNotificationReferenceAsync(
        Guid targetId,
        MentionTargetType targetType,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MentionSearchResponse>> SearchAsync(
        Guid currentUserId,
        string keyword,
        CancellationToken cancellationToken);
}
