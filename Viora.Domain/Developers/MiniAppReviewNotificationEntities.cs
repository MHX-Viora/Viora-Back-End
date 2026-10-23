using Viora.Domain.Entities;

namespace Viora.Domain.Developers;

public enum MiniAppReviewNotificationType
{
    ReviewSubmitted, ReviewStarted, ChangesRequested, ReviewRejected, ReviewApproved,
    Published, Unpublished, Suspended, Restored
}
public enum MiniAppNotificationChannel { Email }
public enum MiniAppNotificationDeliveryStatus { Pending, Processing, Sent, Failed }
public enum MiniAppNotificationDeliveryError { None, NotConfigured, TransportUnavailable, RecipientRejected, RetryExhausted, LeaseExpired }

public sealed class MiniAppReviewNotification : CreatedEntity
{
    public Guid RecipientDeveloperId { get; set; }
    public Guid EventId { get; set; }
    public MiniAppReviewNotificationType EventType { get; set; }
    public Guid MiniAppId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public Guid? MiniAppVersionId { get; set; }
    public Guid? PublicationId { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class MiniAppReviewNotificationDelivery : CreatedEntity
{
    public Guid NotificationId { get; set; }
    public Guid EventId { get; set; }
    public Guid RecipientDeveloperId { get; set; }
    public MiniAppNotificationChannel Channel { get; set; }
    public MiniAppNotificationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public MiniAppNotificationDeliveryError LastErrorCode { get; set; }
}
