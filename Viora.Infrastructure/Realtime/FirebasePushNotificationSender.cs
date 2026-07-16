using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using Viora.Application.Realtime;

namespace Viora.Infrastructure.Realtime;

public sealed class FirebasePushNotificationSender(
    IDeviceTokenRepository deviceTokenRepository,
    IFirebaseMessagingClientFactory firebaseMessagingClientFactory,
    ILogger<FirebasePushNotificationSender> logger) : IPushNotificationSender
{
    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        var client = firebaseMessagingClientFactory.CreateClient();
        if (client is null)
        {
            logger.LogWarning("Firebase app is not configured. Push skipped for user {UserId}.", message.UserId);
            return;
        }

        var tokens = await deviceTokenRepository.GetActiveByUserIdAsync(message.UserId, cancellationToken);
        if (tokens.Count == 0)
        {
            logger.LogInformation("No active device tokens for user {UserId}.", message.UserId);
            return;
        }

        foreach (var deviceToken in tokens)
        {
            await SendToTokenAsync(client, message, deviceToken.Token, message.UserId, cancellationToken);
        }
    }

    private async Task SendToTokenAsync(
        IFirebaseMessagingClient client,
        PushMessage message,
        string token,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var messageId = await client.SendAsync(message, token, cancellationToken);
            logger.LogInformation(
                "Firebase push sent successfully. UserId: {UserId}, MessageId: {MessageId}, TokenSuffix: {TokenSuffix}.",
                userId,
                messageId,
                GetTokenSuffix(token));
        }
        catch (FirebasePushTokenInvalidException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase token is invalid and will be deactivated. UserId: {UserId}, TokenSuffix: {TokenSuffix}.",
                userId,
                GetTokenSuffix(token));
            await deviceTokenRepository.DeactivateAsync(token, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to send Firebase push notification. UserId: {UserId}, TokenSuffix: {TokenSuffix}.",
                userId,
                GetTokenSuffix(token));
        }
    }

    private static string GetTokenSuffix(string token) =>
        token.Length <= 8 ? token : token[^8..];
}

public interface IFirebaseMessagingClientFactory
{
    IFirebaseMessagingClient? CreateClient();
}

public interface IFirebaseMessagingClient
{
    Task<string> SendAsync(PushMessage message, string token, CancellationToken cancellationToken);
}

public sealed class FirebaseMessagingClientFactory(IFirebaseInitializer firebaseInitializer) : IFirebaseMessagingClientFactory
{
    public IFirebaseMessagingClient? CreateClient()
    {
        var app = firebaseInitializer.GetApp();
        return app is null ? null : new FirebaseMessagingClient(app);
    }
}

public sealed class FirebaseMessagingClient(FirebaseApp app) : IFirebaseMessagingClient
{
    public async Task<string> SendAsync(PushMessage message, string token, CancellationToken cancellationToken)
    {
        try
        {
            return await FirebaseMessaging.GetMessaging(app).SendAsync(
                BuildFirebaseMessage(message, token),
                dryRun: false,
                cancellationToken);
        }
        catch (FirebaseMessagingException exception) when (IsInvalidToken(exception))
        {
            throw new FirebasePushTokenInvalidException(exception);
        }
    }

    public static Message BuildFirebaseMessage(PushMessage message, string token) => new()
    {
        Token = token,
        Notification = new Notification
        {
            Title = message.Title,
            Body = message.Body
        },
        Data = message.Data.ToDictionary(pair => pair.Key, pair => pair.Value),
        Android = new AndroidConfig
        {
            Priority = Priority.High,
            Notification = new AndroidNotification
            {
                ChannelId = "default",
                Sound = "default",
                DefaultSound = true
            }
        }
    };

    private static bool IsInvalidToken(FirebaseMessagingException exception) =>
        exception.MessagingErrorCode is MessagingErrorCode.InvalidArgument
            or MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch;
}

public sealed class FirebasePushTokenInvalidException(Exception innerException)
    : Exception("Firebase push token is invalid.", innerException);
