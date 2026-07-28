using Viora.Application.Realtime;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class GroupCallRealtimeContractTests
{
    [Fact]
    public void Group_call_realtime_events_are_stable()
    {
        Assert.Equal("GroupCallStarted", RealtimeEvents.GroupCallStarted);
        Assert.Equal("GroupCallEnded", RealtimeEvents.GroupCallEnded);
    }
}
