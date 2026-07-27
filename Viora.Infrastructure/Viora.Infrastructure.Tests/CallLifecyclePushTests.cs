using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CallLifecyclePushTests
{
    [Theory]
    [InlineData("CallRejected")]
    [InlineData("CallCancelled")]
    [InlineData("CallEnded")]
    [InlineData("CallMissed")]
    [InlineData("CallTimeout")]
    public void BuildFirebaseMessage_call_lifecycle_is_short_lived_data_only(string type)
    {
        var message = new PushMessage(
            Guid.NewGuid(),
            "Call ended",
            null,
            new Dictionary<string, string>
            {
                ["type"] = type,
                ["callId"] = Guid.NewGuid().ToString()
            });

        var firebaseMessage = FirebaseMessagingClient.BuildFirebaseMessage(
            message,
            "fcm-token",
            DevicePlatform.Android);

        Assert.Null(firebaseMessage.Notification);
        Assert.Null(firebaseMessage.Android!.Notification);
        Assert.Equal(TimeSpan.FromSeconds(30), firebaseMessage.Android.TimeToLive);
    }
}
