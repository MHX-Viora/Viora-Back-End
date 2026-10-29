using Viora.Application.Advertisements;
using Xunit;

public sealed class AdvertisementDurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void AcceptsUpToThirtyDays(int days)
    {
        var start = DateTime.UtcNow;
        AdvertisementRules.RequireSchedule(start, start.AddDays(days));
    }

    [Fact]
    public void RejectsMoreThanThirtyDays()
    {
        var start = DateTime.UtcNow;
        var error = Assert.Throws<AdvertisementValidationException>(() => AdvertisementRules.RequireSchedule(start, start.AddDays(30).AddSeconds(1)));
        Assert.Equal("ADVERTISEMENT_DURATION_TOO_LONG", error.Code);
    }
}
