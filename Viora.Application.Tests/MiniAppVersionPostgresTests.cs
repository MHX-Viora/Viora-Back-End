using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppVersionPostgresTests
{
    private sealed class ExpireDuringAudit(SandboxClock clock) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added)) clock.Now += TimeSpan.FromHours(5);
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [Fact]
    public async Task SourceExpiryDuringAuditRollsBackNewVersion()
    {
        await using var f = await SandboxFixture.Create();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Db.Database.GetDbConnection())
            .AddInterceptors(new ExpireDuringAudit(f.Clock)).Options;
        await using var db = new AppDbContext(options); var service = new MiniAppVersionService(db, new(db, f.Clock), f.Clock);
        Assert.Equal(403, (await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CreateAsync(f.Actor.Id, f.Source.Id,
            f.App.AppId, new(Guid.NewGuid(), f.App.Version, "1.0"), default))).Status);
        Assert.Empty(await f.Db.MiniAppVersions.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.MiniAppReviewActions.AsNoTracking().ToListAsync());
        Assert.Equal(f.App.Version, await f.Db.MiniApps.AsNoTracking().Select(x => x.Version).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentDraftCreationHasOneWinnerAndNextVersionPreservesSubmittedSnapshot()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "AddMiniAppReviewNotifications");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var requests = new[] { new MiniAppVersionCreateRequest(Guid.NewGuid(), f.App.Version, "1.0"), new MiniAppVersionCreateRequest(Guid.NewGuid(), f.App.Version, "1.0") };
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(MiniAppVersionSummaryDto? Version, MiniAppVersionCreateRequest Request)> Create(MiniAppVersionCreateRequest request)
        {
            await using var db = new AppDbContext(options); var service = new MiniAppVersionService(db, new(db, f.Clock), f.Clock);
            if (Interlocked.Increment(ref ready) == 2) start.SetResult(); await start.Task;
            try { return (await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, request, default), request); }
            catch (DeveloperAuthException e) when (e.Status == 409) { return (null, request); }
        }
        var winner = Assert.Single(await Task.WhenAll(requests.Select(Create)), x => x.Version != null);
        var first = winner.Version!; var service = MiniAppVersionTests.Service(f);
        Assert.Equal(first, await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, winner.Request, default));
        Assert.Single(await f.Db.MiniAppReviewActions.ToListAsync());
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppVersions\" SET \"State\"=1, \"SubmittedAt\"={DateTime.UtcNow} WHERE \"Id\"={first.Id}");
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppVersions\" SET \"VersionName\"='tampered' WHERE \"Id\"='{first.Id}'");
        var appRevision = await f.Db.MiniApps.Select(x => x.Version).SingleAsync();
        var second = await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), appRevision, "2.0"), default);
        Assert.Equal(2, second.VersionCode);
        Assert.Equal(first.SnapshotHash, (await service.GetAsync(f.Actor.Id, f.Source.Id, f.App.AppId, first.Id, default)).Version.SnapshotHash);
    }

    private sealed class AuditFailure(bool wrappedTransient = false) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<MiniAppReviewAction>().Any(x => x.State == EntityState.Added))
            {
                if (wrappedTransient) throw new InvalidOperationException("provider-transient-fixture",
                    new Npgsql.PostgresException("serialization-fixture", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.SerializationFailure));
                throw new InvalidOperationException("stage7-fixture-audit-failure");
            }
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditFailureRollsBackVersionAllocationAndAppRevision(bool wrappedTransient)
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "AddMiniAppReviewNotifications");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).AddInterceptors(new AuditFailure(wrappedTransient)).Options;
        await using var db = new AppDbContext(options); var service = new MiniAppVersionService(db, new(db, f.Clock), f.Clock);
        Func<Task> create = async () => await service.CreateAsync(f.Actor.Id, f.Source.Id, f.App.AppId, new(Guid.NewGuid(), f.App.Version, "1.0"), default);
        if (wrappedTransient) Assert.Equal(409, (await Assert.ThrowsAsync<DeveloperAuthException>(create)).Status);
        else Assert.Equal("stage7-fixture-audit-failure", (await Assert.ThrowsAsync<InvalidOperationException>(create)).Message);
        Assert.Empty(await f.Db.MiniAppVersions.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.MiniAppReviewActions.AsNoTracking().ToListAsync());
        Assert.Equal(f.App.Version, await f.Db.MiniApps.AsNoTracking().Select(x => x.Version).SingleAsync());
    }
}
