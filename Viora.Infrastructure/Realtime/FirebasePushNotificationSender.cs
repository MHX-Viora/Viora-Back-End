using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Viora.Application.Realtime;
using DevicePlatform = Viora.Domain.Entities.DevicePlatform;

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
            await SendToTokenAsync(
                client,
                message,
                deviceToken.Token,
                deviceToken.Platform,
                message.UserId,
                cancellationToken);
        }
    }

    private async Task SendToTokenAsync(
        IFirebaseMessagingClient client,
        PushMessage message,
        string token,
        DevicePlatform platform,
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

            var messageId = await client.SendAsync(message, token, platform, cancellationToken);
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
                "Firebase token send failed. UserId: {UserId}, NotificationType: {NotificationType}, MessagingErrorCode: {MessagingErrorCode}, ErrorCode: {ErrorCode}, ShouldDeactivate: {ShouldDeactivate}, TokenSuffix: {TokenSuffix}, DeviceTokenHash: {DeviceTokenHash}.",
                userId,
                notificationType,
                exception.MessagingErrorCode,
                exception.ErrorCode,
                exception.ShouldDeactivate,
                tokenSuffix,
                tokenHash);
            if (exception.ShouldDeactivate)
            {
                await deviceTokenRepository.DeactivateAsync(token, cancellationToken);
            }
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
    Task<string> SendAsync(
        PushMessage message,
        string token,
        DevicePlatform platform,
        CancellationToken cancellationToken);
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
    public async Task<string> SendAsync(
        PushMessage message,
        string token,
        DevicePlatform platform,
        CancellationToken cancellationToken)
    {
        try
        {
            var messageId = await FirebaseMessaging.GetMessaging(app).SendAsync(
                BuildFirebaseMessage(message, token, platform),
                dryRun: false,
                cancellationToken);

            return messageId ?? string.Empty;
        }
        catch (FirebaseMessagingException exception) when (IsInvalidToken(exception))
        {
            throw new FirebasePushTokenInvalidException(exception);
        }
    }

    public static Message BuildFirebaseMessage(
        PushMessage message,
        string token,
        DevicePlatform platform)
    {
        var isAndroidChat =
            platform == DevicePlatform.Android &&
            message.Data.TryGetValue("type", out var type) &&
            type == "chat";
        var isAndroidIncomingCall =
            platform == DevicePlatform.Android &&
            message.Data.TryGetValue("type", out var messageType) &&
            messageType == "IncomingCall";
        var isCallLifecycle =
            message.Data.TryGetValue("type", out var lifecycleType) &&
            lifecycleType is "CallRejected" or "CallCancelled" or "CallEnded" or "CallMissed" or "CallTimeout";
        var isAndroidDataOnly = isAndroidChat || isAndroidIncomingCall;
        var isDataOnly = isAndroidDataOnly || isCallLifecycle;
        var data = message.Data.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (isDataOnly)
        {
            data["title"] = message.Title;
            data["body"] = message.Body ?? string.Empty;
        }

        return new Message
        {
            Token = token,
            Notification = isDataOnly
                ? null
                : new Notification
                {
                    Title = message.Title,
                    Body = message.Body
                },
            Data = data,
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                TimeToLive = isAndroidIncomingCall || isCallLifecycle
                    ? TimeSpan.FromSeconds(30)
                    : TimeSpan.FromHours(4),
                Notification = isDataOnly
                    ? null
                    : new AndroidNotification
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
                    ["apns-priority"] = isCallLifecycle ? "5" : "10"
                },
                Aps = new Aps
                {
                    ContentAvailable = isCallLifecycle,
                    Sound = isCallLifecycle ? null : "default"
                }
            }
        };
    }

    private static bool IsInvalidToken(FirebaseMessagingException exception) =>
        exception.MessagingErrorCode is MessagingErrorCode.InvalidArgument
            or MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch;
}

public sealed class FirebasePushTokenInvalidException : Exception
{
    public FirebasePushTokenInvalidException(
        string messagingErrorCode,
        string? errorCode,
        bool shouldDeactivate,
        Exception innerException)
        : base("Firebase push token is invalid.", innerException)
    {
        MessagingErrorCode = messagingErrorCode;
        ErrorCode = errorCode;
        HttpStatusCode = null;
        ShouldDeactivate = shouldDeactivate;
    }

    public FirebasePushTokenInvalidException(Exception innerException)
        : base("Firebase push token is invalid.", innerException)
    {
        if (innerException is FirebaseMessagingException firebaseException)
        {
            MessagingErrorCode = firebaseException.MessagingErrorCode?.ToString() ?? "UnknownFirebaseMessagingError";
            ErrorCode = firebaseException.ErrorCode.ToString();
            HttpStatusCode = firebaseException.HttpResponse?.StatusCode.ToString();
            ShouldDeactivate = firebaseException.MessagingErrorCode == FirebaseAdmin.Messaging.MessagingErrorCode.Unregistered;
            return;
        }

        MessagingErrorCode = innerException.GetType().Name;
        ErrorCode = null;
        HttpStatusCode = null;
        ShouldDeactivate = false;
    }

    public string MessagingErrorCode { get; }
    public string? ErrorCode { get; }
    public string? HttpStatusCode { get; }
    public bool ShouldDeactivate { get; }
}
