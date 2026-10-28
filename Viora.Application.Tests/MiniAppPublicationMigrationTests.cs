using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Migrations;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Xunit;

public sealed class MiniAppPublicationMigrationTests
{
    [Fact]
    public async Task DowngradeGuardExcludesConcurrentLegacyPublicationWriters()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "AddMiniAppPublications");
        // Exercise only the guard, before DDL happens to acquire its own incidental FK locks.
        var guard = new AddMiniAppPublications().DownOperations.OfType<SqlOperation>().First().Sql;
        await using var downgrade = await f.Db.Database.BeginTransactionAsync();
        await f.Db.Database.ExecuteSqlRawAsync(guard);
        await using var writer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options);
        await using var writing = await writer.Database.BeginTransactionAsync();
        var conflict = await Assert.ThrowsAsync<PostgresException>(() => writer.Database.ExecuteSqlRawAsync(
            "LOCK TABLE \"MiniApps\" IN ROW EXCLUSIVE MODE NOWAIT"));
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, conflict.SqlState);
        await writing.RollbackAsync(); await downgrade.RollbackAsync();
    }

    [Fact]
    public async Task OnlyApprovedExactTargetsPublishAndDowngradeRequiresExplicitQuarantine()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var (previous, current, migrator) = await MiniAppReviewMigrationTests.Prepare(f.Db, "AddMiniAppPublications");
        var down = migrator.GenerateScript(current, previous, MigrationsSqlGenerationOptions.NoTransactions);
        var version = MiniAppVersionPersistenceTests.Version(f.App);
        version.State = MiniAppVersionState.Submitted; version.SubmittedAt = DateTime.UtcNow;
        var admin = new Account { Email = "publication-pg@example.test" };
        f.Db.AddRange(version, admin); await f.Db.SaveChangesAsync();
        var review = MiniAppReviewPersistenceTests.Review(f.App, version.Id);
        f.Db.Add(review); await f.Db.SaveChangesAsync();
        var publication = MiniAppPublicationPersistenceTests.Publication(f.App, review);
        f.Db.Add(publication); await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewRequests\" SET \"Status\"=1, \"AssignedAdminAccountId\"={admin.Id}, \"ReviewStartedAt\"={DateTime.UtcNow} WHERE \"Id\"={review.Id}");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewRequests\" SET \"Status\"=4, \"DecidedAt\"={DateTime.UtcNow} WHERE \"Id\"={review.Id}");
        f.Db.Add(publication); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppPublications\" SET \"MiniAppVersionId\"=gen_random_uuid() WHERE \"Id\"='{publication.Id}'");
        await MiniAppReviewMigrationTests.Denied(f.Db, $"DELETE FROM \"MiniAppPublications\" WHERE \"Id\"='{publication.Id}'");
        await MiniAppReviewMigrationTests.Denied(f.Db, down);
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppPublications\" SET \"Status\"=1, \"UnpublishedAt\"={DateTime.UtcNow} WHERE \"Id\"={publication.Id}");
        await MiniAppReviewMigrationTests.Denied(f.Db, $"UPDATE \"MiniAppPublications\" SET \"Status\"=0, \"UnpublishedAt\"=NULL WHERE \"Id\"='{publication.Id}'");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniApps\" SET \"Status\"=6 WHERE \"Id\"={f.App.Id}");
        await MiniAppReviewMigrationTests.Denied(f.Db, down);
        // Explicit operator quarantine for the isolated fixture; never done to application rows.
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniApps\" SET \"Status\"=7 WHERE \"Id\"={f.App.Id}");
        await using (var downgrade = await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.Database.ExecuteSqlRawAsync(down);
            await downgrade.CommitAsync();
        }
        Assert.Single(await f.Db.MiniAppVersions.ToListAsync());
        Assert.Single(await f.Db.MiniAppReviewRequests.ToListAsync());
        Assert.Single(await f.Db.MiniAppOAuthClients.ToListAsync());
        Assert.Single(await f.Db.DeveloperSessions.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }
}
