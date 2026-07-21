using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
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
            logger.LogWarning(
                "Firebase app is not configured. Push skipped for user {UserId}. NotificationType: {NotificationType}.",
                message.UserId,
                GetNotificationType(message));
            return;
        }

        var tokens = await deviceTokenRepository.GetActiveByUserIdAsync(message.UserId, cancellationToken);
        logger.LogInformation(
            "Dispatching Firebase push. UserId: {UserId}, NotificationType: {NotificationType}, ActiveTokenCount: {ActiveTokenCount}, FirebaseProjectId: {FirebaseProjectId}.",
            message.UserId,
            GetNotificationType(message),
            tokens.Count,
            firebaseMessagingClientFactory.ProjectId ?? "unknown");

        var validTokens = tokens
            .Where(deviceToken => !string.IsNullOrWhiteSpace(deviceToken.Token))
            .ToArray();

        var blankTokenCount = tokens.Count - validTokens.Length;
        if (blankTokenCount > 0)
        {
            logger.LogWarning(
                "Skipping blank Firebase device token(s). UserId: {UserId}, NotificationType: {NotificationType}, BlankTokenCount: {BlankTokenCount}.",
                message.UserId,
                GetNotificationType(message),
                blankTokenCount);
        }

        if (validTokens.Length == 0)
        {
            logger.LogInformation(
                "No active Firebase device tokens for user {UserId}. NotificationType: {NotificationType}.",
                message.UserId,
                GetNotificationType(message));
            return;
        }

        foreach (var deviceToken in validTokens)
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
        var notificationType = GetNotificationType(message);
        var tokenSuffix = GetTokenSuffix(token);
        var tokenHash = GetTokenHash(token);

        try
        {
            logger.LogInformation(
                "Sending Firebase push. UserId: {UserId}, NotificationType: {NotificationType}, TokenSuffix: {TokenSuffix}, DeviceTokenHash: {DeviceTokenHash}.",
                userId,
                notificationType,
                tokenSuffix,
                tokenHash);

            var messageId = await client.SendAsync(message, token, cancellationToken);
            logger.LogInformation(
                "Firebase push sent successfully. UserId: {UserId}, NotificationType: {NotificationType}, FirebaseMessageId: {FirebaseMessageId}, TokenSuffix: {TokenSuffix}, DeviceTokenHash: {DeviceTokenHash}.",
                userId,
                notificationType,
                messageId,
                tokenSuffix,
                tokenHash);
        }
        catch (FirebasePushTokenInvalidException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase token is invalid and will be deactivated. UserId: {UserId}, NotificationType: {NotificationType}, FirebaseError: {FirebaseError}, TokenSuffix: {TokenSuffix}, DeviceTokenHash: {DeviceTokenHash}.",
                userId,
                notificationType,
                exception.FirebaseError,
                tokenSuffix,
                tokenHash);
            await deviceTokenRepository.DeactivateAsync(token, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to send Firebase push notification. UserId: {UserId}, NotificationType: {NotificationType}, FirebaseError: {FirebaseError}, TokenSuffix: {TokenSuffix}, DeviceTokenHash: {DeviceTokenHash}.",
                userId,
                notificationType,
                GetFirebaseError(exception),
                tokenSuffix,
                tokenHash);
        }
    }

    private static string GetTokenSuffix(string token) =>
        token.Length <= 8 ? token : token[^8..];

    private static string GetTokenHash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash)[..16];
    }

    private static string GetNotificationType(PushMessage message)
    {
        if (message.Data.TryGetValue("notificationType", out var notificationType) &&
            !string.IsNullOrWhiteSpace(notificationType))
        {
            return notificationType;
        }

        return message.Data.TryGetValue("type", out var type) && !string.IsNullOrWhiteSpace(type)
            ? type
            : "unknown";
    }

    private static string GetFirebaseError(Exception exception) =>
        exception is FirebaseMessagingException firebaseException
            ? firebaseException.MessagingErrorCode?.ToString() ?? "UnknownFirebaseError"
            : exception.GetType().Name;
}

public interface IFirebaseMessagingClientFactory
{
    string? ProjectId { get; }
    IFirebaseMessagingClient? CreateClient();
}

public interface IFirebaseMessagingClient
{
    Task<string> SendAsync(PushMessage message, string token, CancellationToken cancellationToken);
}

public sealed class FirebaseMessagingClientFactory(IFirebaseInitializer firebaseInitializer) : IFirebaseMessagingClientFactory
{
    public string? ProjectId => firebaseInitializer.ProjectId;

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
            var messageId = await FirebaseMessaging.GetMessaging(app).SendAsync(
                BuildFirebaseMessage(message, token),
                dryRun: false,
                cancellationToken);

            return messageId ?? string.Empty;
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
            TimeToLive = TimeSpan.FromHours(4),
            Notification = new AndroidNotification
            {
                ChannelId = "default",
                Sound = "default",
                DefaultSound = true
            }
        },
        Apns = new ApnsConfig
        {
            Headers = new Dictionary<string, string>
            {
                ["apns-priority"] = "10"
            },
            Aps = new Aps
            {
                Sound = "default"
            }
        }
    };

    private static bool IsInvalidToken(FirebaseMessagingException exception) =>
        exception.MessagingErrorCode is MessagingErrorCode.InvalidArgument
            or MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch;
}

public sealed class FirebasePushTokenInvalidException : Exception
{
    public FirebasePushTokenInvalidException(Exception innerException)
        : base("Firebase push token is invalid.", innerException)
    {
        FirebaseError = innerException is FirebaseMessagingException firebaseException
            ? firebaseException.MessagingErrorCode?.ToString() ?? "UnknownFirebaseError"
            : innerException.GetType().Name;
    }

    public string FirebaseError { get; }
}
