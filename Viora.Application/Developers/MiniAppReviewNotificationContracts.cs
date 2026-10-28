namespace Viora.Application.Developers;

public sealed record MiniAppReviewNotificationDto(Guid Id, string EventType, string AppId, Guid? ReviewId,
    Guid? VersionId, Guid? PublicationId, string DeepLink, DateTime CreatedAt, DateTime? ReadAt);
public sealed record MiniAppReviewNotificationPageDto(IReadOnlyList<MiniAppReviewNotificationDto> Items,
    int UnreadCount, int Page, int PageSize, int Total);
public sealed record MiniAppReviewNotificationReadRequest;
