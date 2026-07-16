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
            await SendToTokenAsync(client, message, deviceToken.Token, cancellationToken);
        }
    }

    private async Task SendToTokenAsync(
        IFirebaseMessagingClient client,
        PushMessage message,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.SendAsync(message, token, cancellationToken);
        }
        catch (FirebasePushTokenInvalidException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase token is invalid and will be deactivated.");
            await deviceTokenRepository.DeactivateAsync(token, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send Firebase push notification.");
        }
    }
}

public interface IFirebaseMessagingClientFactory
{
    IFirebaseMessagingClient? CreateClient();
}

public interface IFirebaseMessagingClient
{
    Task SendAsync(PushMessage message, string token, CancellationToken cancellationToken);
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
    public async Task SendAsync(PushMessage message, string token, CancellationToken cancellationToken)
    {
        try
        {
            await FirebaseMessaging.GetMessaging(app).SendAsync(new Message
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
            throw new FirebasePushTokenInvalidException(exception);
        }
    }

    private static bool IsInvalidToken(FirebaseMessagingException exception) =>
        exception.MessagingErrorCode is MessagingErrorCode.InvalidArgument
            or MessagingErrorCode.Unregistered
            or MessagingErrorCode.SenderIdMismatch;
}

public sealed class FirebasePushTokenInvalidException(Exception innerException)
    : Exception("Firebase push token is invalid.", innerException);
