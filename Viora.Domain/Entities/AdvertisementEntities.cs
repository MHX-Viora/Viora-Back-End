namespace Viora.Domain.Entities;

public sealed class Advertisement : AuditableEntity
{
    public Guid PostId { get; set; }
    public Guid AdvertiserId { get; set; }
    public Guid? CampaignId { get; set; }
    public AdvertisementPlacement Placement { get; set; }
    public AdvertisementObjective Objective { get; set; }
    public AdvertisementDestinationType DestinationType { get; set; }
    public string? DestinationUrl { get; set; }
    public AdvertisementCtaType CtaType { get; set; }
    public AdvertisementTargetingMode TargetingMode { get; set; }
    public short? MinimumAge { get; set; }
    public short? MaximumAge { get; set; }
    public string? TargetLocation { get; set; }
    public decimal? DailyBudget { get; set; }
    public decimal TotalBudget { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal ReservedAmount { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public AdvertisementStatus Status { get; set; } = AdvertisementStatus.Draft;
    public string? ReviewReason { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Post Post { get; set; } = null!;
    public User Advertiser { get; set; } = null!;
    public User? Reviewer { get; set; }
    public ICollection<AdvertisementEvent> Events { get; set; } = [];
    public ICollection<AdvertisementFeedback> Feedback { get; set; } = [];
}

public sealed class AdvertisementEvent : CreatedEntity
{
    public Guid AdvertisementId { get; set; }
    public Guid ViewerId { get; set; }
    public AdvertisementEventType Type { get; set; }
    public string ClientEventId { get; set; } = null!;
    public decimal ChargeAmount { get; set; }
    public Advertisement Advertisement { get; set; } = null!;
    public User Viewer { get; set; } = null!;
}

public sealed class AdvertisementFeedback : CreatedEntity
{
    public Guid AdvertisementId { get; set; }
    public Guid ViewerId { get; set; }
    public AdvertisementFeedbackType Type { get; set; }
    public string? Reason { get; set; }
    public Advertisement Advertisement { get; set; } = null!;
    public User Viewer { get; set; } = null!;
}
