using Viora.Domain.Entities;

namespace Viora.Application.Advertisements;

public class AdvertisementValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AdvertisementConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class AdvertisementRules
{
    public const decimal MinimumBudget = 50_000m;
    public const decimal MaximumBudget = 1_000_000_000m;

    public static string? NormalizeDestinationUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new AdvertisementValidationException(
                "INVALID_AD_DESTINATION",
                "Liên kết quảng cáo phải là địa chỉ HTTPS hợp lệ.");
        }

        return uri.AbsoluteUri;
    }

    public static void RequireCtaDestination(AdvertisementCtaType ctaType, string? destinationUrl)
    {
        if (ctaType is AdvertisementCtaType.Message or AdvertisementCtaType.ContactNow or AdvertisementCtaType.Follow)
        {
            if (destinationUrl is not null)
                throw new AdvertisementValidationException("INVALID_AD_CTA_DESTINATION", "CTA này phải mở đích đến trong ứng dụng.");
        }
        else if (ctaType is AdvertisementCtaType.BuyNow or AdvertisementCtaType.SignUp or
                 AdvertisementCtaType.Download or AdvertisementCtaType.ViewProduct or AdvertisementCtaType.GetOffer)
        {
            if (destinationUrl is null)
                throw new AdvertisementValidationException("AD_DESTINATION_REQUIRED", "CTA này cần liên kết HTTPS.");
        }
    }

    public static void RequireBudget(decimal budget)
    {
        if (budget < MinimumBudget)
            throw new AdvertisementValidationException("ADVERTISEMENT_BUDGET_TOO_LOW", $"Ngân sách tối thiểu là {MinimumBudget:N0}đ.");
        if (budget > MaximumBudget)
            throw new AdvertisementValidationException("ADVERTISEMENT_BUDGET_TOO_HIGH", $"Ngân sách tối đa là {MaximumBudget:N0}đ.");
        if (decimal.Round(budget, 0) != budget)
            throw new AdvertisementValidationException("INVALID_ADVERTISEMENT_BUDGET", "Ngân sách quảng cáo phải là số VND nguyên.");
    }

    public static void RequireSchedule(DateTime startAt, DateTime endAt)
    {
        if (endAt <= startAt)
            throw new AdvertisementValidationException("INVALID_ADVERTISEMENT_SCHEDULE", "Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (endAt - startAt > TimeSpan.FromDays(90))
            throw new AdvertisementValidationException("ADVERTISEMENT_DURATION_TOO_LONG", "Chiến dịch không được dài quá 90 ngày.");
    }

    public static void RequireTransition(AdvertisementStatus current, AdvertisementStatus next)
    {
        var allowed = (current, next) switch
        {
            (AdvertisementStatus.Draft, AdvertisementStatus.Pending) => true,
            (AdvertisementStatus.Pending, AdvertisementStatus.Approved) => true,
            (AdvertisementStatus.Pending, AdvertisementStatus.Rejected) => true,
            (AdvertisementStatus.Approved, AdvertisementStatus.Active) => true,
            (AdvertisementStatus.Active, AdvertisementStatus.Paused) => true,
            (AdvertisementStatus.Paused, AdvertisementStatus.Active) => true,
            (AdvertisementStatus.Active, AdvertisementStatus.Completed) => true,
            (AdvertisementStatus.Approved, AdvertisementStatus.Completed) => true,
            (AdvertisementStatus.Paused, AdvertisementStatus.Completed) => true,
            (AdvertisementStatus.Draft, AdvertisementStatus.Cancelled) => true,
            (AdvertisementStatus.Pending, AdvertisementStatus.Cancelled) => true,
            (AdvertisementStatus.Approved, AdvertisementStatus.Cancelled) => true,
            (AdvertisementStatus.Active, AdvertisementStatus.Cancelled) => true,
            (AdvertisementStatus.Paused, AdvertisementStatus.Cancelled) => true,
            _ => false
        };
        if (!allowed)
            throw new AdvertisementConflictException("INVALID_ADVERTISEMENT_TRANSITION", "Không thể chuyển quảng cáo sang trạng thái này.");
    }

    public static void RequireReviewReason(AdvertisementStatus next, string? reason)
    {
        if (next == AdvertisementStatus.Rejected && string.IsNullOrWhiteSpace(reason))
            throw new AdvertisementValidationException("ADVERTISEMENT_REJECTION_REASON_REQUIRED", "Cần nhập lý do từ chối quảng cáo.");
        if (reason?.Trim().Length > 500)
            throw new AdvertisementValidationException("ADVERTISEMENT_REVIEW_REASON_TOO_LONG", "Lý do kiểm duyệt không được vượt quá 500 ký tự.");
    }

    public static decimal CalculateCharge(decimal unitPrice, decimal spentAmount, decimal totalBudget, decimal? dailyRemaining = null)
    {
        if (unitPrice <= 0 || spentAmount >= totalBudget) return 0m;
        var charge = Math.Min(unitPrice, totalBudget - spentAmount);
        return dailyRemaining.HasValue ? Math.Min(charge, Math.Max(0m, dailyRemaining.Value)) : charge;
    }

    public static void RequireDailyBudgetTotal(decimal? dailyBudget, decimal totalBudget, DateTime startAt, DateTime endAt)
    {
        if (!dailyBudget.HasValue || endAt <= startAt) return;
        var days = (decimal)Math.Ceiling((endAt - startAt).TotalDays);
        if (dailyBudget.Value * days != totalBudget)
            throw new AdvertisementValidationException("INVALID_ADVERTISEMENT_TOTAL_BUDGET", "Tổng ngân sách phải bằng ngân sách ngày nhân số ngày chạy.");
    }

    public static bool IsRepeatedImpression(DateTime lastImpressionAt, DateTime now) =>
        lastImpressionAt > now.AddHours(-24);
}
