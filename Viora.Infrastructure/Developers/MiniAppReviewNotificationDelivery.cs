using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppReviewEmailDelivery(AppDbContext db, IDeveloperEmailSender sender, TimeProvider clock, IOptions<DeveloperAuthOptions> options)
{
    private IQueryable<MiniAppReviewNotificationDelivery> Available(DateTime now) => db.MiniAppReviewNotificationDeliveries.Where(x =>
        x.Channel == MiniAppNotificationChannel.Email && (x.Status == MiniAppNotificationDeliveryStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
            || x.Status == MiniAppNotificationDeliveryStatus.Processing && x.LeaseUntil <= now));

    public async Task<int> DrainAsync(CancellationToken ct, int batchSize = 100)
    {
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Email dispatch cannot join a business transaction.");
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await Available(now).AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.Id).Take(batchSize).ToListAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested(); now = clock.GetUtcNow().UtcDateTime;
            if (await Available(now).Where(x => x.Id == id && x.AttemptCount >= 5).ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.Status, MiniAppNotificationDeliveryStatus.Failed).SetProperty(x => x.LeaseId, (Guid?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTime?)null).SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                    .SetProperty(x => x.LastErrorCode, MiniAppNotificationDeliveryError.RetryExhausted), ct) == 1) { processed++; continue; }
            var lease = Guid.NewGuid(); var until = now.AddMinutes(2);
            if (await Available(now).Where(x => x.Id == id && x.AttemptCount < 5).ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.Status, MiniAppNotificationDeliveryStatus.Processing).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LeaseId, (Guid?)lease).SetProperty(x => x.LeaseUntil, until)
                    .SetProperty(x => x.NextAttemptAt, (DateTime?)null), ct) != 1) continue;
            processed++;
            var row = await db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            if (row.LeaseId != lease || row.LeaseUntil <= clock.GetUtcNow().UtcDateTime) continue;
            var error = await SendAsync(row.NotificationId, ct); var completed = clock.GetUtcNow().UtcDateTime;
            var sent = error == MiniAppNotificationDeliveryError.None;
            var failed = !sent && (row.AttemptCount >= 5 || error is MiniAppNotificationDeliveryError.NotConfigured or MiniAppNotificationDeliveryError.RecipientRejected);
            var status = sent ? MiniAppNotificationDeliveryStatus.Sent : failed ? MiniAppNotificationDeliveryStatus.Failed : MiniAppNotificationDeliveryStatus.Pending;
            var finalError = failed && row.AttemptCount >= 5 ? MiniAppNotificationDeliveryError.RetryExhausted : error;
            DateTime? next = sent || failed ? null : completed.AddMinutes(Math.Pow(2, row.AttemptCount - 1));
            DateTime? sentAt = sent ? completed : null;
            // An expired/replaced lease never acknowledges or overwrites another worker's result.
            await db.MiniAppReviewNotificationDeliveries.Where(x => x.Id == id && x.Status == MiniAppNotificationDeliveryStatus.Processing
                && x.LeaseId == lease && x.LeaseUntil > completed).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
                    .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null)
                    .SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.SentAt, sentAt).SetProperty(x => x.LastErrorCode, finalError), ct);
        }
        return processed;
    }

    private async Task<MiniAppNotificationDeliveryError> SendAsync(Guid notificationId, CancellationToken ct)
    {
        if (!sender.IsConfigured || !Uri.TryCreate(options.Value.PortalUrl, UriKind.Absolute, out var portal) || portal.Scheme != "https"
            || portal.UserInfo.Length != 0 || portal.Query.Length != 0 || portal.Fragment.Length != 0) return MiniAppNotificationDeliveryError.NotConfigured;
        var message = await (from notification in db.MiniAppReviewNotifications.AsNoTracking()
            join recipient in db.DeveloperAccounts.AsNoTracking() on notification.RecipientDeveloperId equals recipient.Id
            join app in db.MiniApps.AsNoTracking() on notification.MiniAppId equals app.Id
            where notification.Id == notificationId select new { recipient.EmailNormalized, app.AppId, notification.EventType }).SingleAsync(ct);
        if (!MailboxAddress.TryParse(message.EmailNormalized, out _)) return MiniAppNotificationDeliveryError.RecipientRejected;
        var link = portal.AbsoluteUri.TrimEnd('/') + "/console/apps/" + Uri.EscapeDataString(message.AppId) + "?tab=review";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await sender.SendAsync(message.EmailNormalized, "ANKT Mini App: " + message.EventType,
                "Mini App " + message.AppId + ": " + message.EventType + ".\nXem chi tiết trong Developer Console: " + link, timeout.Token);
            return MiniAppNotificationDeliveryError.None;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (FormatException) { return MiniAppNotificationDeliveryError.RecipientRejected; }
        catch { return MiniAppNotificationDeliveryError.TransportUnavailable; }
    }
}
