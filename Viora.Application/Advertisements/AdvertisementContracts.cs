using Viora.Domain.Entities;

namespace Viora.Application.Advertisements;

public sealed class AdvertisementNotFoundException(string code = "ADVERTISEMENT_NOT_FOUND", string message = "Không tìm thấy quảng cáo.") : Exception(message)
{
    public string Code { get; } = code;
}
public sealed class AdvertisementForbiddenException(string message) : Exception(message);
public sealed class AdvertisementInsufficientBalanceException(decimal available, decimal required)
    : AdvertisementValidationException("INSUFFICIENT_WALLET_BALANCE", "Số dư của bạn không đủ để chạy quảng cáo.")
{
    public decimal Available { get; } = available;
    public decimal Required { get; } = required;
    public decimal Shortfall { get; } = Math.Max(0m, required - available);
}

public sealed record CreateAdvertisementRequest(
    Guid PostId,
    AdvertisementObjective Objective,
    string? DestinationUrl,
    AdvertisementCtaType CtaType,
    AdvertisementTargetingMode TargetingMode,
    short? MinimumAge,
    short? MaximumAge,
    string? TargetLocation,
    decimal? DailyBudget,
    decimal TotalBudget,
    DateTime StartAt,
    DateTime EndAt);

public sealed record AdvertisementUserResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified,
    AccountStyle AccountStyle);

public sealed record AdvertisementMediaResponse(Guid Id, string MediaUrl, string? ThumbnailUrl);

public sealed record AdvertisementArticleResponse(
    string Title,
    string? ThumbnailUrl,
    string? Preview,
    int ReadingTimeMinutes);

public sealed record AdvertisementContentResponse(
    Guid Id,
    PostType PostType,
    string? Content,
    string? Location,
    string? Link,
    DateTime CreatedAt,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    AdvertisementUserResponse User,
    IReadOnlyList<AdvertisementMediaResponse> Media,
    IReadOnlyList<string> Hashtags,
    AdvertisementArticleResponse? Article);

public sealed record AdvertisementResponse(
    Guid Id,
    Guid PostId,
    Guid AdvertiserId,
    AdvertisementPlacement Placement,
    AdvertisementObjective Objective,
    AdvertisementDestinationType DestinationType,
    string? DestinationUrl,
    AdvertisementCtaType CtaType,
    AdvertisementTargetingMode TargetingMode,
    short? MinimumAge,
    short? MaximumAge,
    string? TargetLocation,
    decimal? DailyBudget,
    decimal TotalBudget,
    decimal SpentAmount,
    decimal ReservedAmount,
    DateTime StartAt,
    DateTime EndAt,
    AdvertisementStatus Status,
    string? ReviewReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Impressions,
    long Clicks,
    decimal ClickThroughRate,
    AdvertisementContentResponse Content);

public sealed record AdvertisementPage(
    IReadOnlyList<AdvertisementResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record AdvertisementDeliveryResponse(IReadOnlyList<AdvertisementResponse> Items);
public sealed record AdvertisementEventRequest(string ClientEventId);
public sealed record AdvertisementEventResponse(Guid Id, bool IsDuplicate, decimal ChargeAmount, decimal SpentAmount, decimal RemainingBudget, AdvertisementStatus Status);
public sealed record AdvertisementFeedbackRequest(AdvertisementFeedbackType Type, string? Reason);
public sealed record AdvertisementFeedbackResponse(Guid Id, AdvertisementFeedbackType Type);

public sealed class AdvertisementOptions
{
    public decimal ImpressionPrice { get; set; } = 100m;
    public decimal ClickPrice { get; set; } = 500m;
    public int MaximumDeliveryItems { get; set; } = 10;
}

public interface IAdvertisementService
{
    Task<AdvertisementResponse> CreateAsync(Guid userId, CreateAdvertisementRequest request, CancellationToken cancellationToken);
    Task<AdvertisementResponse> SubmitAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken);
    Task<AdvertisementResponse> PauseAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken);
    Task<AdvertisementResponse> ResumeAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken);
    Task<AdvertisementResponse> CancelAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken);
    Task<AdvertisementResponse> GetAsync(Guid userId, Guid advertisementId, CancellationToken cancellationToken);
    Task<AdvertisementPage> GetMineAsync(Guid userId, int page, int pageSize, AdvertisementStatus? status, CancellationToken cancellationToken);
    Task<AdvertisementDeliveryResponse> GetDeliveryAsync(Guid viewerId, AdvertisementPlacement placement, int take, CancellationToken cancellationToken);
    Task<AdvertisementEventResponse> RecordEventAsync(Guid viewerId, Guid advertisementId, AdvertisementEventType type, AdvertisementEventRequest request, CancellationToken cancellationToken);
    Task<AdvertisementFeedbackResponse> RecordFeedbackAsync(Guid viewerId, Guid advertisementId, AdvertisementFeedbackRequest request, CancellationToken cancellationToken);
    Task<AdvertisementPage> GetAdminPageAsync(int page, int pageSize, AdvertisementStatus? status, CancellationToken cancellationToken);
    Task<AdvertisementResponse> ApproveAsync(Guid adminId, Guid advertisementId, CancellationToken cancellationToken);
    Task<AdvertisementResponse> RejectAsync(Guid adminId, Guid advertisementId, string reason, CancellationToken cancellationToken);
    Task SynchronizeLifecycleAsync(CancellationToken cancellationToken);
}
