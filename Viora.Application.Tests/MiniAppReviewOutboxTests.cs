using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppReviewOutboxTests
{
    internal sealed class Sender : IDeveloperEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public int Calls; public string? Body; public Func<Task>? OnSend;
        public async Task SendAsync(string recipient, string subject, string text, CancellationToken ct)
        { Calls++; Body = text; Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase); if (OnSend != null) await OnSend(); }
    }
    internal static MiniAppReviewEmailDelivery Service(SandboxFixture f, Sender sender) => new(f.Db, sender, f.Clock,
        Options.Create(new DeveloperAuthOptions { PortalUrl = "https://portal.ankt.example/developers" }));
    internal static async Task Seed(SandboxFixture f)
    {
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        var action = await new MiniAppReviewAuditService(f.Db).AppendAsync(new()
        { MiniAppId = f.App.Id, Actor = new(MiniAppReviewActorType.Developer, f.Actor.Id), Action = MiniAppReviewActionKind.ReviewSubmitted,
            RequestId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), PayloadHash = MiniAppReviewAuditService.Hash(new { command = "outbox-fixture" }) }, default);
        await new MiniAppReviewNotificationService(f.Db, f.Clock).QueueAsync(action.Id, null, default); await tx.CommitAsync();
    }

    [Fact]
    public async Task CommittedDeliveryIsSentOnceAndNeverCreatesAnotherInboxEntry()
    {
        await using var f = await SandboxFixture.Create(); await Seed(f); var sender = new Sender(); var service = Service(f, sender);
        Assert.Equal(1, await service.DrainAsync(default)); Assert.Equal(0, await service.DrainAsync(default)); Assert.Equal(1, sender.Calls);
        var row = await f.Db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(MiniAppNotificationDeliveryStatus.Sent, row.Status); Assert.Equal(1, row.AttemptCount); Assert.NotNull(row.SentAt); Assert.Null(row.LeaseId);
        Assert.Contains("https://portal.ankt.example/developers/console/apps/", sender.Body);
        Assert.Single(await f.Db.MiniAppReviewNotifications.ToListAsync());
    }

    [Fact]
    public async Task MissingSmtpFailsExplicitlyAndTransientFailuresStopAfterFiveAttempts()
    {
        await using var f = await SandboxFixture.Create(); await Seed(f); var sender = new Sender { IsConfigured = false }; var service = Service(f, sender);
        await service.DrainAsync(default); Assert.Equal(0, sender.Calls);
        var row = await f.Db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(MiniAppNotificationDeliveryStatus.Failed, row.Status); Assert.Equal(MiniAppNotificationDeliveryError.NotConfigured, row.LastErrorCode);
        // Explicit fixture reset models an operator retry; no public reset API is introduced.
        await f.Db.MiniAppReviewNotificationDeliveries.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppNotificationDeliveryStatus.Pending)
            .SetProperty(x => x.AttemptCount, 0).SetProperty(x => x.NextAttemptAt, (DateTime?)null));
        sender.IsConfigured = true; sender.OnSend = () => throw new InvalidOperationException("fixture transport unavailable");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(1, await service.DrainAsync(default)); Assert.Equal(0, await service.DrainAsync(default)); f.Clock.Now += TimeSpan.FromMinutes(20);
        }
        row = await f.Db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(5, row.AttemptCount); Assert.Equal(MiniAppNotificationDeliveryStatus.Failed, row.Status);
        Assert.Equal(MiniAppNotificationDeliveryError.RetryExhausted, row.LastErrorCode); Assert.Equal(5, sender.Calls);
    }

    [Fact]
    public async Task ExpiredFifthLeaseFailsWithoutSendingAndOwnedTransactionsCannotDispatch()
    {
        await using var f = await SandboxFixture.Create(); await Seed(f); var sender = new Sender(); var service = Service(f, sender);
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DrainAsync(default));
        var lease = Guid.NewGuid();
        await f.Db.MiniAppReviewNotificationDeliveries.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MiniAppNotificationDeliveryStatus.Processing)
            .SetProperty(x => x.AttemptCount, 5).SetProperty(x => x.LeaseId, (Guid?)lease).SetProperty(x => x.LeaseUntil, f.Clock.Now.UtcDateTime.AddSeconds(-1)));
        Assert.Equal(1, await service.DrainAsync(default)); Assert.Equal(0, sender.Calls);
        Assert.Equal(MiniAppNotificationDeliveryStatus.Failed, await f.Db.MiniAppReviewNotificationDeliveries.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ReplacedLeaseCannotBeAcknowledgedAndExpiredLeaseCanBeRecovered()
    {
        await using var f = await SandboxFixture.Create(); await Seed(f); var replacement = Guid.NewGuid();
        var sender = new Sender { OnSend = async () =>
        {
            await f.Db.MiniAppReviewNotificationDeliveries.ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseId, (Guid?)replacement)
                .SetProperty(x => x.LeaseUntil, f.Clock.Now.UtcDateTime.AddMinutes(3)));
        } };
        var service = Service(f, sender); await service.DrainAsync(default);
        var row = await f.Db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(replacement, row.LeaseId); Assert.Equal(MiniAppNotificationDeliveryStatus.Processing, row.Status); Assert.Null(row.SentAt);
        f.Clock.Now += TimeSpan.FromMinutes(4); sender.OnSend = null; await service.DrainAsync(default);
        row = await f.Db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(2, row.AttemptCount); Assert.Equal(MiniAppNotificationDeliveryStatus.Sent, row.Status);
        Assert.Single(await f.Db.MiniAppReviewNotifications.ToListAsync());
    }

    [Fact]
    public async Task EachBatchProcessesAtMostOneHundredPersistedDeliveries()
    {
        await using var f = await SandboxFixture.Create(); for (var i = 0; i < 101; i++) await Seed(f);
        var sender = new Sender(); var service = Service(f, sender);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.DrainAsync(default, 101));
        Assert.Equal(100, await service.DrainAsync(default)); Assert.Equal(100, sender.Calls);
        Assert.Equal(1, await service.DrainAsync(default)); Assert.Equal(101, sender.Calls);
        Assert.Equal(101, await f.Db.MiniAppReviewNotifications.CountAsync());
    }
}
