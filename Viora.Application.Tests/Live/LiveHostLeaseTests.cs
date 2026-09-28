using Viora.Application.Live;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class LiveHostLeaseTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LiveLifecycleOptions Options = new();

    [Fact]
    public void LiveSessionExpiresAfterHostStopsSendingHeartbeats()
    {
        Assert.False(LiveHostLease.IsStale(LiveStatus.Live, Now.AddSeconds(-119), Now.AddHours(-1), Now.AddHours(-1), Now, Options));
        Assert.True(LiveHostLease.IsStale(LiveStatus.Live, Now.AddSeconds(-121), Now.AddHours(-1), Now.AddHours(-1), Now, Options));
    }

    [Fact]
    public void OldSessionWithoutHeartbeatExpiresButPreparingGetsLongerGrace()
    {
        Assert.True(LiveHostLease.IsStale(LiveStatus.Live, null, Now.AddMinutes(-3), Now.AddMinutes(-4), Now, Options));
        Assert.False(LiveHostLease.IsStale(LiveStatus.Preparing, null, null, Now.AddMinutes(-9), Now, Options));
        Assert.True(LiveHostLease.IsStale(LiveStatus.Preparing, null, null, Now.AddMinutes(-11), Now, Options));
    }

    [Fact]
    public void CompletedLiveNeverExpiresAgain()
    {
        Assert.False(LiveHostLease.IsStale(LiveStatus.Ended, null, Now.AddHours(-2), Now.AddHours(-2), Now, Options));
    }
}
