using Microsoft.Extensions.Logging;
using Viora.Application.Realtime;

namespace Viora.Infrastructure.Realtime;

public sealed class NoOpPushNotificationSender(ILogger<NoOpPushNotificationSender> logger) : IPushNotificationSender
{
    public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Push notification skipped for user {UserId}. Configure Firebase sender to enable FCM.",
            message.UserId);
        return Task.CompletedTask;
    }
}
