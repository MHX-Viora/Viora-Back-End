using Viora.Application.Notifications;
using Viora.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Viora.Application.Mentions;

public sealed class MentionService(
    IMentionRepository repository,
    INotificationService notificationService,
    ILogger<MentionService> logger) : IMentionService
{
    public async Task<IReadOnlyList<MentionResponse>> CreateAsync(
        Guid mentionedByUserId,
        Guid targetId,
        MentionTargetType targetType,
        IReadOnlyList<Guid>? mentionedUserIds,
        CancellationToken cancellationToken)
    {
        var ids = (mentionedUserIds ?? [])
            .Where(id => id != Guid.Empty && id != mentionedByUserId)
            .Distinct()
            .ToArray();
        if (ids.Length == 0) return [];

        var sender = await repository.GetActiveUserAsync(mentionedByUserId, cancellationToken);
        if (sender is null) return [];

        var users = await repository.GetEligibleUsersAsync(mentionedByUserId, ids, cancellationToken);
        var mentions = users.Select(user => new Mention
        {
            MentionedUserId = user.Id,
            MentionedUser = user,
            MentionedByUserId = sender.Id,
            MentionedByUser = sender,
            TargetId = targetId,
            TargetType = targetType
        }).ToArray();

        if (mentions.Length == 0) return [];
        await repository.AddRangeAsync(mentions, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var notificationReference = await repository.GetNotificationReferenceAsync(
            targetId, targetType, cancellationToken);
        foreach (var user in users)
        {
            var targetLabel = targetType switch
            {
                MentionTargetType.Post => "bài viết",
                MentionTargetType.Comment => "bình luận",
                MentionTargetType.Reply => "phản hồi",
                _ => "tin nhắn"
            };
            try
            {
                await notificationService.SendAsync(new SendNotificationCommand(
                    user.Id,
                    NotificationType.Mention,
                    sender,
                    notificationReference?.Id,
                    notificationReference?.Type,
                    ImageUrl: null,
                    Title: sender.DisplayName,
                    Content: $"đã nhắc đến bạn trong một {targetLabel}."), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Mention notification delivery failed for user {MentionedUserId} and target {TargetId}.",
                    user.Id, targetId);
            }
        }

        return users.Select(user => new MentionResponse(user.Id, user.DisplayName)).ToArray();
    }

    public Task<IReadOnlyList<MentionSearchResponse>> SearchAsync(
        Guid currentUserId,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var value = keyword?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? Task.FromResult<IReadOnlyList<MentionSearchResponse>>([])
            : repository.SearchAsync(currentUserId, value, cancellationToken);
    }
}
