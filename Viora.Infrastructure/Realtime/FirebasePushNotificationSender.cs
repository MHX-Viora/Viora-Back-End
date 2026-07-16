using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using Viora.Application.Realtime;

namespace Viora.Infrastructure.Realtime;

public sealed class FirebasePushNotificationSender(
    IDeviceTokenRepository deviceTokenRepository,
    IFirebaseInitializer firebaseInitializer,
    ILogger<FirebasePushNotificationSender> logger) : IPushNotificationSender
{
    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        var app = firebaseInitializer.GetApp();
        if (app is null)
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

        var messaging = FirebaseMessaging.GetMessaging(app);
        foreach (var deviceToken in tokens)
        {
            await SendToTokenAsync(messaging, message, deviceToken.Token, cancellationToken);
        }
    }

    private async Task SendToTokenAsync(
        FirebaseMessaging messaging,
        PushMessage message,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            await messaging.SendAsync(new Message
            {
                Token = token,
                Notification = new Notification
                {
                    Title = message.Title,
                    Body = message.Body
                },
                Data = message.Data.ToDictionary(pair => pair.Key, pair => pair.Value)
            }, dryRun: false, cancellationToken);
        }
        catch (FirebaseMessagingException exception) when (IsInvalidToken(exception))
        {
            logger.LogWarning(
                exception,
                "Firebase token is invalid and will be deactivated. Messaging error: {ErrorCode}.",
                exception.MessagingErrorCode);
            await deviceTokenRepository.DeactivateAsync(token, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send Firebase push notification.");
        }
    }

    private static bool IsInvalidToken(FirebaseMessagingException exception) =>
        exception.MessagingErrorCode is MessagingErrorCode.InvalidArgument
            or MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch;
}
