using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewOutboxPostgresTests
{
    [Fact]
    public async Task TwoWorkersCannotSendTheSameLiveLease()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname"); await MiniAppReviewOutboxTests.Seed(f);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var sender = new MiniAppReviewOutboxTests.Sender(); var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Drain()
        {
            await using var db = new AppDbContext(options);
            var service = new MiniAppReviewEmailDelivery(db, sender, f.Clock, Options.Create(new DeveloperAuthOptions { PortalUrl = "https://portal.ankt.example/developers" }));
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task; return await service.DrainAsync(default);
        }
        Assert.Equal(1, (await Task.WhenAll(Drain(), Drain())).Sum()); Assert.Equal(1, sender.Calls);
        var row = await f.Db.MiniAppReviewNotificationDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(MiniAppNotificationDeliveryStatus.Sent, row.Status); Assert.Equal(1, row.AttemptCount);
    }

    [Fact]
    public async Task UncommittedBusinessEventIsInvisibleToDeliveryWorker()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using var reader = new AppDbContext(options); var sender = new MiniAppReviewOutboxTests.Sender();
        var service = new MiniAppReviewEmailDelivery(reader, sender, f.Clock, Options.Create(new DeveloperAuthOptions { PortalUrl = "https://portal.ankt.example/developers" }));
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            var action = await new MiniAppReviewAuditService(f.Db).AppendAsync(new()
            { MiniAppId = f.App.Id, Actor = new(MiniAppReviewActorType.Developer, f.Actor.Id), Action = MiniAppReviewActionKind.ReviewSubmitted,
                RequestId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), PayloadHash = MiniAppReviewAuditService.Hash(new { command = "uncommitted-fixture" }) }, default);
            await new MiniAppReviewNotificationService(f.Db, f.Clock).QueueAsync(action.Id, null, default);
            Assert.Equal(0, await service.DrainAsync(default)); Assert.Equal(0, sender.Calls); await tx.CommitAsync();
        }
        Assert.Equal(1, await service.DrainAsync(default)); Assert.Equal(1, sender.Calls);
    }
}
