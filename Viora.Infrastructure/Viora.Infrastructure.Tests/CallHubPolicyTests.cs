using System.Reflection;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CallHubPolicyTests
{
    [Theory]
    [InlineData("ReceiveIceCandidate")]
    [InlineData("ReconnectCall")]
    public void Shared_signals_require_an_accepted_call(string eventName)
    {
        Assert.True(CanForward(eventName, CallStatus.Accepted, true));
        Assert.True(CanForward(eventName, CallStatus.Accepted, false));
        Assert.False(CanForward(eventName, CallStatus.Calling, true));
        Assert.False(CanForward(eventName, CallStatus.Ended, false));
    }

    [Theory]
    [InlineData("ReceiveOffer", true)]
    [InlineData("ReceiveAnswer", false)]
    [InlineData("CallAccepted", false)]
    public void Directed_signals_require_the_correct_role(string eventName, bool isCaller)
    {
        Assert.True(CanForward(eventName, CallStatus.Accepted, isCaller));
        Assert.False(CanForward(eventName, CallStatus.Accepted, !isCaller));
    }

    [Theory]
    [InlineData("CallEnded")]
    [InlineData("CallRejected")]
    [InlineData("CallCancelled")]
    public void Client_cannot_publish_server_owned_lifecycle_events(string eventName)
    {
        Assert.False(CanForward(eventName, CallStatus.Accepted, true));
        Assert.False(CanForward(eventName, CallStatus.Accepted, false));
    }

    private static bool CanForward(string eventName, CallStatus status, bool isCaller)
    {
        var method = typeof(CallHub).GetMethod(
            "CanForward",
            BindingFlags.NonPublic | BindingFlags.Static);

        return Assert.IsType<bool>(method!.Invoke(null, [eventName, status, isCaller]));
    }
}
