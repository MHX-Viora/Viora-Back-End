using Viora.Application.Advertisements;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Advertisements;

public sealed class AdvertisementRulesTests
{
    [Fact]
    public void Personal_account_cannot_advertise() =>
        Assert.False(AccountStyle.Personal.CanAdvertise());

    [Theory]
    [InlineData(AccountStyle.Creator)]
    [InlineData(AccountStyle.Journalist)]
    [InlineData(AccountStyle.Business)]
    [InlineData(AccountStyle.Organization)]
    [InlineData(AccountStyle.Agency)]
    public void Professional_accounts_can_advertise(AccountStyle style) =>
        Assert.True(style.CanAdvertise());

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com")]
    public void Destination_must_be_https(string destinationUrl)
    {
        var exception = Assert.Throws<AdvertisementValidationException>(
            () => AdvertisementRules.NormalizeDestinationUrl(destinationUrl));

        Assert.Equal("INVALID_AD_DESTINATION", exception.Code);
    }

    [Fact]
    public void Empty_destination_uses_in_app_content() =>
        Assert.Null(AdvertisementRules.NormalizeDestinationUrl("  "));

    [Fact]
    public void Message_cta_cannot_open_an_external_website()
    {
        var exception = Assert.Throws<AdvertisementValidationException>(() =>
            AdvertisementRules.RequireCtaDestination(AdvertisementCtaType.Message, "https://example.com/"));
        Assert.Equal("INVALID_AD_CTA_DESTINATION", exception.Code);
    }

    [Fact]
    public void Purchase_cta_requires_a_website()
    {
        var exception = Assert.Throws<AdvertisementValidationException>(() =>
            AdvertisementRules.RequireCtaDestination(AdvertisementCtaType.BuyNow, null));
        Assert.Equal("AD_DESTINATION_REQUIRED", exception.Code);
    }

    [Fact]
    public void Budget_below_minimum_is_rejected()
    {
        var exception = Assert.Throws<AdvertisementValidationException>(
            () => AdvertisementRules.RequireBudget(49_999m));

        Assert.Equal("ADVERTISEMENT_BUDGET_TOO_LOW", exception.Code);
    }

    [Fact]
    public void End_must_be_after_start()
    {
        var start = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var exception = Assert.Throws<AdvertisementValidationException>(
            () => AdvertisementRules.RequireSchedule(start, start));

        Assert.Equal("INVALID_ADVERTISEMENT_SCHEDULE", exception.Code);
    }

    [Theory]
    [InlineData(AdvertisementStatus.Draft, AdvertisementStatus.Pending)]
    [InlineData(AdvertisementStatus.Pending, AdvertisementStatus.Approved)]
    [InlineData(AdvertisementStatus.Pending, AdvertisementStatus.Rejected)]
    [InlineData(AdvertisementStatus.Approved, AdvertisementStatus.Active)]
    [InlineData(AdvertisementStatus.Active, AdvertisementStatus.Paused)]
    [InlineData(AdvertisementStatus.Paused, AdvertisementStatus.Active)]
    [InlineData(AdvertisementStatus.Active, AdvertisementStatus.Completed)]
    public void Valid_transitions_are_allowed(AdvertisementStatus current, AdvertisementStatus next) =>
        AdvertisementRules.RequireTransition(current, next);

    [Fact]
    public void Rejected_advertisement_cannot_be_activated()
    {
        var exception = Assert.Throws<AdvertisementConflictException>(
            () => AdvertisementRules.RequireTransition(AdvertisementStatus.Rejected, AdvertisementStatus.Active));

        Assert.Equal("INVALID_ADVERTISEMENT_TRANSITION", exception.Code);
    }

    [Fact]
    public void Rejection_requires_a_reason()
    {
        var exception = Assert.Throws<AdvertisementValidationException>(
            () => AdvertisementRules.RequireReviewReason(AdvertisementStatus.Rejected, " "));

        Assert.Equal("ADVERTISEMENT_REJECTION_REASON_REQUIRED", exception.Code);
    }

    [Fact]
    public void Charge_is_capped_by_remaining_budget()
    {
        Assert.Equal(30m, AdvertisementRules.CalculateCharge(100m, 970m, 1_000m));
        Assert.Equal(0m, AdvertisementRules.CalculateCharge(100m, 1_000m, 1_000m));
        Assert.Equal(20m, AdvertisementRules.CalculateCharge(100m, 0m, 1_000m, 20m));
        Assert.Equal(0m, AdvertisementRules.CalculateCharge(100m, 0m, 1_000m, 0m));
    }

    [Fact]
    public void Impression_is_counted_at_most_once_per_viewer_per_day()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(AdvertisementRules.IsRepeatedImpression(now.AddHours(-23), now));
        Assert.False(AdvertisementRules.IsRepeatedImpression(now.AddHours(-24), now));
    }
}
