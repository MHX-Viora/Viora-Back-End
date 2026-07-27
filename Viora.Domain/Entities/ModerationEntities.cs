namespace Viora.Domain.Entities;

public sealed class Notification : CreatedEntity
{
    public Guid UserId { get; set; }
    public Guid? SenderUserId { get; set; }
    public NotificationType NotificationType { get; set; }
    public Guid? ReferenceId { get; set; }
    public NotificationReferenceType? ReferenceType { get; set; }
    public string Title { get; set; } = null!;
    public string? Content { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsRead { get; set; }
    public User User { get; set; } = null!;
    public User? SenderUser { get; set; }
}

public sealed class Mention : CreatedEntity
{
    public Guid MentionedUserId { get; set; }
    public Guid MentionedByUserId { get; set; }
    public Guid TargetId { get; set; }
    public MentionTargetType TargetType { get; set; }
    public User MentionedUser { get; set; } = null!;
    public User MentionedByUser { get; set; } = null!;
}

public sealed class Report : CreatedEntity
{
    public Guid ReporterUserId { get; set; }
    public Guid TargetId { get; set; }
    public ReportTargetType TargetType { get; set; }
    public ReportReason Reason { get; set; } = ReportReason.Other;
    public string? Description { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public User ReporterUser { get; set; } = null!;
    public User? Reviewer { get; set; }
}

public sealed class AdminLog : CreatedEntity
{
    public Guid AdminId { get; set; }
    public string Action { get; set; } = null!;
    public string TargetType { get; set; } = null!;
    public Guid? TargetId { get; set; }
    public string? Description { get; set; }
    public User Admin { get; set; } = null!;
}
