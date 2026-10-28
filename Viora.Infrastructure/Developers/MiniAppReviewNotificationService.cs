using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// Persists receipts only. A separate worker may deliver email after the owning transaction commits.
public sealed class MiniAppReviewNotificationService(AppDbContext db, TimeProvider clock)
{
    public async Task<MiniAppReviewNotificationPageDto> ListAsync(Guid owner, Guid source, int page, int pageSize, bool unread, CancellationToken ct)
    {
        await new MiniAppReviewAccess(db, clock).RequireDeveloperAsync(owner, source, ct);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100) throw MiniAppInput.Invalid("Phân trang không hợp lệ.");
        var visible = Visible(owner); var unreadCount = await visible.CountAsync(x => x.ReadAt == null, ct);
        var query = unread ? visible.Where(x => x.ReadAt == null) : visible; var total = await query.CountAsync(ct);
        var rows = await (from receipt in query join app in db.MiniApps.AsNoTracking() on receipt.MiniAppId equals app.Id
            orderby receipt.CreatedAt descending, receipt.Id descending select new { Receipt = receipt, app.AppId })
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        await new MiniAppReviewAccess(db, clock).RequireDeveloperAsync(owner, source, ct);
        return new(rows.Select(x => Map(x.Receipt, x.AppId)).ToArray(), unreadCount, page, pageSize, total);
    }
    public async Task<MiniAppReviewNotificationDto> MarkReadAsync(Guid owner, Guid source, Guid id, CancellationToken ct)
    {
        var access = new MiniAppReviewAccess(db, clock); await access.RequireDeveloperAsync(owner, source, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await Visible(owner).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw MiniAppReviewAccess.NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        if (now < receipt.CreatedAt) now = receipt.CreatedAt;
        if (receipt.ReadAt == null) await Visible(owner).Where(x => x.Id == id && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, (DateTime?)now), ct);
        receipt = await Visible(owner).SingleAsync(x => x.Id == id, ct);
        var appId = await db.MiniApps.Where(x => x.Id == receipt.MiniAppId).Select(x => x.AppId).SingleAsync(ct);
        await access.RequireDeveloperAsync(owner, source, ct); await tx.CommitAsync(ct); return Map(receipt, appId);
    }
    private IQueryable<MiniAppReviewNotification> Visible(Guid owner) => db.MiniAppReviewNotifications.AsNoTracking().Where(x => x.RecipientDeveloperId == owner &&
        db.MiniApps.Any(app => app.Id == x.MiniAppId && app.DeveloperAccountId == owner && app.DeletedAt == null));
    private static MiniAppReviewNotificationDto Map(MiniAppReviewNotification receipt, string appId)
    {
        var link = "/developers/console/apps/" + Uri.EscapeDataString(appId) + "?tab=review";
        if (receipt.ReviewRequestId is { } review) link += "&reviewId=" + review.ToString("D");
        if (receipt.MiniAppVersionId is { } version) link += "&versionId=" + version.ToString("D");
        if (receipt.PublicationId is { } publication) link += "&publicationId=" + publication.ToString("D");
        return new(receipt.Id, receipt.EventType.ToString(), appId, receipt.ReviewRequestId, receipt.MiniAppVersionId, receipt.PublicationId, link, receipt.CreatedAt, receipt.ReadAt);
    }
    public async Task<MiniAppReviewNotification> QueueAsync(Guid eventId, Guid? publicationId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Review notifications require the command transaction.");
        var action = await db.MiniAppReviewActions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, ct) ?? throw Invalid();
        var type = action.Action switch
        {
            MiniAppReviewActionKind.ReviewSubmitted => MiniAppReviewNotificationType.ReviewSubmitted,
            MiniAppReviewActionKind.ReviewStarted => MiniAppReviewNotificationType.ReviewStarted,
            MiniAppReviewActionKind.ChangesRequested => MiniAppReviewNotificationType.ChangesRequested,
            MiniAppReviewActionKind.ReviewRejected => MiniAppReviewNotificationType.ReviewRejected,
            MiniAppReviewActionKind.ReviewApproved => MiniAppReviewNotificationType.ReviewApproved,
            MiniAppReviewActionKind.Published => MiniAppReviewNotificationType.Published,
            MiniAppReviewActionKind.Unpublished => MiniAppReviewNotificationType.Unpublished,
            MiniAppReviewActionKind.Suspended => MiniAppReviewNotificationType.Suspended,
            MiniAppReviewActionKind.Restored => MiniAppReviewNotificationType.Restored,
            _ => throw Invalid()
        };
        var recipient = await db.MiniApps.AsNoTracking().Where(x => x.Id == action.MiniAppId).Select(x => x.DeveloperAccountId).SingleAsync(ct);
        if (publicationId is { } publication && !await db.MiniAppPublications.AnyAsync(x => x.Id == publication && x.MiniAppId == action.MiniAppId
            && (action.MiniAppVersionId == null || x.MiniAppVersionId == action.MiniAppVersionId), ct)) throw Invalid();
        var previous = await db.MiniAppReviewNotifications.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == eventId && x.RecipientDeveloperId == recipient, ct);
        if (previous != null)
        {
            if (previous.PublicationId != publicationId || previous.EventType != type || previous.ReviewRequestId != action.ReviewRequestId
                || previous.MiniAppVersionId != action.MiniAppVersionId) throw Invalid();
            return previous;
        }
        var notification = new MiniAppReviewNotification { RecipientDeveloperId = recipient, EventId = eventId, EventType = type,
            MiniAppId = action.MiniAppId, ReviewRequestId = action.ReviewRequestId, MiniAppVersionId = action.MiniAppVersionId, PublicationId = publicationId };
        db.MiniAppReviewNotifications.Add(notification);
        db.MiniAppReviewNotificationDeliveries.Add(new() { NotificationId = notification.Id, EventId = eventId, RecipientDeveloperId = recipient,
            Channel = MiniAppNotificationChannel.Email, Status = MiniAppNotificationDeliveryStatus.Pending, NextAttemptAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct); return notification;
    }
    private static DeveloperAuthException Invalid() => new(409, "REVIEW_NOTIFICATION_CONFLICT", "Sự kiện thông báo xét duyệt không hợp lệ.");
}
