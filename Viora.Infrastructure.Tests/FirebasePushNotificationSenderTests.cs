using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class FirebasePushNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_without_active_tokens_does_not_send()
    {
        var repository = new FakeDeviceTokenRepository();
        var client = new FakeFirebaseMessagingClient();
        var sender = CreateSender(repository, client);

        await sender.SendAsync(CreateMessage(), CancellationToken.None);

        Assert.Empty(client.SentTokens);
    }

    [Fact]
    public async Task SendAsync_with_active_token_sends_firebase_message()
    {
        var userId = Guid.NewGuid();
        var token = new DeviceToken { UserId = userId, Token = "fcm-token", IsActive = true };
        var repository = new FakeDeviceTokenRepository(token);
        var client = new FakeFirebaseMessagingClient();
        var sender = CreateSender(repository, client);
        var message = CreateMessage(userId);

        await sender.SendAsync(message, CancellationToken.None);

        Assert.Equal(["fcm-token"], client.SentTokens);
        var sentMessage = Assert.Single(client.SentMessages);
        Assert.Equal(message.Title, sentMessage.Title);
        Assert.Equal(message.Body, sentMessage.Body);
        Assert.Equal(message.Data["id"], sentMessage.Data["id"]);
    }

    [Fact]
    public async Task SendAsync_with_blank_active_token_skips_it()
    {
        var userId = Guid.NewGuid();
        var blankToken = new DeviceToken { UserId = userId, Token = " ", IsActive = true };
        var validToken = new DeviceToken { UserId = userId, Token = "fcm-token", IsActive = true };
        var repository = new FakeDeviceTokenRepository(blankToken, validToken);
        var client = new FakeFirebaseMessagingClient();
        var sender = CreateSender(repository, client);

        await sender.SendAsync(CreateMessage(userId), CancellationToken.None);

        Assert.Equal(["fcm-token"], client.SentTokens);
    }

    [Fact]
    public async Task SendAsync_deactivates_invalid_token()
    {
        var userId = Guid.NewGuid();
        var token = new DeviceToken { UserId = userId, Token = "dead-token", IsActive = true };
        var repository = new FakeDeviceTokenRepository(token);
        var client = new FakeFirebaseMessagingClient { InvalidToken = "dead-token" };
        var sender = CreateSender(repository, client);

        await sender.SendAsync(CreateMessage(userId), CancellationToken.None);

        Assert.False(token.IsActive);
        Assert.Contains("dead-token", repository.DeactivatedTokens);
    }

    [Fact]
    public void BuildFirebaseMessage_includes_android_and_apns_notification_payloads()
    {
        var message = CreateMessage();

        var firebaseMessage = FirebaseMessagingClient.BuildFirebaseMessage(message, "fcm-token");

        Assert.Equal("fcm-token", firebaseMessage.Token);
        Assert.NotNull(firebaseMessage.Notification);
        Assert.Equal(message.Title, firebaseMessage.Notification.Title);
        Assert.Equal(message.Body, firebaseMessage.Notification.Body);
        Assert.Equal(message.Data["id"], firebaseMessage.Data["id"]);
        Assert.NotNull(firebaseMessage.Android);
        Assert.Equal(FirebaseAdmin.Messaging.Priority.High, firebaseMessage.Android.Priority);
        Assert.Equal(TimeSpan.FromHours(4), firebaseMessage.Android.TimeToLive);
        Assert.NotNull(firebaseMessage.Android.Notification);
        Assert.Equal("default", firebaseMessage.Android.Notification.ChannelId);
        Assert.Equal("default", firebaseMessage.Android.Notification.Sound);
        Assert.NotNull(firebaseMessage.Apns);
        Assert.Equal("10", firebaseMessage.Apns.Headers["apns-priority"]);
        Assert.NotNull(firebaseMessage.Apns.Aps);
        Assert.Equal("default", firebaseMessage.Apns.Aps.Sound);
    }

    private static FirebasePushNotificationSender CreateSender(
        FakeDeviceTokenRepository repository,
        FakeFirebaseMessagingClient? client) =>
        new(
            repository,
            new FakeFirebaseMessagingClientFactory(client),
            NullLogger<FirebasePushNotificationSender>.Instance);

    private static PushMessage CreateMessage(Guid? userId = null) => new(
        userId ?? Guid.NewGuid(),
        "Title",
        "Body",
        new Dictionary<string, string>
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = "1"
        });

    private sealed class FakeFirebaseMessagingClientFactory(IFirebaseMessagingClient? client)
        : IFirebaseMessagingClientFactory
    {
        public IFirebaseMessagingClient? CreateClient() => client;
    }

    private sealed class FakeFirebaseMessagingClient : IFirebaseMessagingClient
    {
        public string? InvalidToken { get; init; }
        public List<string> SentTokens { get; } = [];
        public List<PushMessage> SentMessages { get; } = [];

        public Task<string> SendAsync(PushMessage message, string token, CancellationToken cancellationToken)
        {
            if (token == InvalidToken)
            {
                throw new FirebasePushTokenInvalidException(new InvalidOperationException("Invalid token."));
            }

            SentTokens.Add(token);
            SentMessages.Add(message);
            return Task.FromResult("projects/viora/messages/test-message-id");
        }
    }

    private sealed class FakeDeviceTokenRepository(params DeviceToken[] tokens) : IDeviceTokenRepository
    {
        private readonly List<DeviceToken> tokens = [.. tokens];

        public List<string> DeactivatedTokens { get; } = [];

        public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(tokens.SingleOrDefault(deviceToken => deviceToken.Token == token));

        public Task<DeviceToken?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(tokens.SingleOrDefault(deviceToken => deviceToken.DeviceId == deviceId));

        public Task<DeviceToken?> GetByTokenOrDeviceIdAsync(string token, string? deviceId, CancellationToken cancellationToken)
        {
            var byToken = tokens.SingleOrDefault(deviceToken => deviceToken.Token == token);
            return Task.FromResult(byToken ?? (deviceId is null
                ? null
                : tokens.SingleOrDefault(deviceToken => deviceToken.DeviceId == deviceId)));
        }

        public Task<IReadOnlyList<DeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeviceToken>>(tokens
                .Where(deviceToken => deviceToken.UserId == userId && deviceToken.IsActive)
                .ToArray());

        public Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken)
        {
            tokens.Add(deviceToken);
            return Task.CompletedTask;
        }

        public Task DeactivateAsync(string token, CancellationToken cancellationToken)
        {
            DeactivatedTokens.Add(token);
            var deviceToken = tokens.SingleOrDefault(item => item.Token == token);
            if (deviceToken is not null)
            {
                deviceToken.IsActive = false;
                deviceToken.LastSeenAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
