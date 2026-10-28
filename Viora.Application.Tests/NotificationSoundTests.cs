using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;
using Xunit;

public sealed class NotificationSoundTests
{
    [Fact]
    public void GeneralPushUsesBundledSoundAndVersionedChannel()
    {
        var message = new PushMessage(Guid.NewGuid(), "Thông báo", "Nội dung", new Dictionary<string, string> { ["type"] = "1" });
        var android = FirebaseMessagingClient.BuildFirebaseMessage(message, "fixture", DevicePlatform.Android);
        Assert.Equal("ankt_notifications_v2", android.Android.Notification.ChannelId);
        Assert.Equal("ankt_notification", android.Android.Notification.Sound);
        Assert.False(android.Android.Notification.DefaultSound);
        var ios = FirebaseMessagingClient.BuildFirebaseMessage(message, "fixture", DevicePlatform.Ios);
        Assert.Equal("ankt_notification.wav", ios.Apns.Aps.Sound);
    }
    [Fact]
    public void ChatAndroidRemainsDataOnlyAndSilentCallLifecycleHasNoAlertSound()
    {
        var chat = new PushMessage(Guid.NewGuid(), "Tin nhắn", "Nội dung", new Dictionary<string, string> { ["type"] = "chat" });
        var android = FirebaseMessagingClient.BuildFirebaseMessage(chat, "fixture", DevicePlatform.Android);
        Assert.Null(android.Notification); Assert.Null(android.Android.Notification);
        Assert.Equal("ankt_notification.wav", FirebaseMessagingClient.BuildFirebaseMessage(chat, "fixture", DevicePlatform.Ios).Apns.Aps.Sound);
        var end = chat with { Data = new Dictionary<string, string> { ["type"] = "CallEnded" } };
        var silent = FirebaseMessagingClient.BuildFirebaseMessage(end, "fixture", DevicePlatform.Ios);
        Assert.Null(silent.Notification); Assert.Null(silent.Apns.Aps.Sound);
    }
}
