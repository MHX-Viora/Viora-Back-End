using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Xunit;

public sealed class MiniAppReviewMigrationTests
{
    [Fact]
    public async Task PostgreSqlAllowsOneActiveReviewAcrossConcurrentSubmissions()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await Prepare(f.Db, "AddMiniAppReviews");
        var versions = new[] { MiniAppVersionPersistenceTests.Version(f.App), MiniAppVersionPersistenceTests.Version(f.App, 2) };
        foreach (var version in versions) { version.State = MiniAppVersionState.Submitted; version.SubmittedAt = DateTime.UtcNow; }
        f.Db.AddRange(versions); await f.Db.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<Viora.Infrastructure.Persistence.AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready = 0; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Submit(Guid versionId)
        {
            await using var db = new Viora.Infrastructure.Persistence.AppDbContext(options);
            db.Add(MiniAppReviewPersistenceTests.Review(f.App, versionId));
            if (Interlocked.Increment(ref ready) == 2) start.SetResult();
            await start.Task;
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return false; }
        }
        Assert.Single(await Task.WhenAll(versions.Select(x => Submit(x.Id))), x => x);
        Assert.Single(await f.Db.MiniAppReviewRequests.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReviewTargetsEvidenceDecisionsAndAuditAreImmutable()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var (previous, current, migrator) = await Prepare(f.Db, "AddMiniAppReviews");
        var version = MiniAppVersionPersistenceTests.Version(f.App);
        version.State = MiniAppVersionState.Submitted; version.SubmittedAt = DateTime.UtcNow;
        var admin = new Account { Email = "review-admin@example.test" };
        f.Db.AddRange(version, admin); await f.Db.SaveChangesAsync();
        var review = MiniAppReviewPersistenceTests.Review(f.App, version.Id);
        var action = MiniAppReviewPersistenceTests.Action(f.App); action.ReviewRequestId = review.Id;
        f.Db.AddRange(review, action); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        foreach (var expression in new[] { "\"MiniAppVersionId\"=gen_random_uuid()", "\"EvidenceAttestationJson\"='[]'", "\"Status\"=4" })
            await Denied(f.Db, $"UPDATE \"MiniAppReviewRequests\" SET {expression} WHERE \"Id\"='{review.Id}'");
        await Denied(f.Db, $"DELETE FROM \"MiniAppReviewActions\" WHERE \"Id\"='{action.Id}'");
        await Denied(f.Db, $"UPDATE \"MiniAppReviewActions\" SET \"Action\"=1 WHERE \"Id\"='{action.Id}'");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewRequests\" SET \"Status\"=1, \"AssignedAdminAccountId\"={admin.Id}, \"ReviewStartedAt\"={DateTime.UtcNow} WHERE \"Id\"={review.Id}");
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppReviewRequests\" SET \"Status\"=4, \"DecidedAt\"={DateTime.UtcNow} WHERE \"Id\"={review.Id}");
        await Denied(f.Db, $"UPDATE \"MiniAppReviewRequests\" SET \"DecisionReason\"='changed after approval' WHERE \"Id\"='{review.Id}'");
        await Denied(f.Db, $"UPDATE \"MiniAppReviewRequests\" SET \"Status\"=3, \"DecisionReason\"='contradictory decision' WHERE \"Id\"='{review.Id}'");
        await Denied(f.Db, $"DELETE FROM \"MiniAppReviewRequests\" WHERE \"Id\"='{review.Id}'");
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Single(await f.Db.MiniAppVersions.ToListAsync());
        Assert.Single(await f.Db.DeveloperSessions.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        Assert.Empty(await f.Db.MiniAppReviewRequests.ToListAsync());
    }

    internal static async Task<(string Previous, string Current, IMigrator Migrator)> Prepare(
        Viora.Infrastructure.Persistence.AppDbContext db, string suffix)
    {
        var current = db.Database.GetMigrations().Single(x => x.EndsWith("_" + suffix));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        // Disposable EnsureCreated database only: rebuild Stage 7 through its real migration chain.
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE IF EXISTS "MiniAppProductionRuntimeEvents", "MiniAppProductionLaunchGrants", "MiniAppProductionHostSessions", "MiniAppCatalogPublications", "MiniAppReviewNotificationDeliveries", "MiniAppReviewNotifications",
                "MiniAppOperationalStates", "MiniAppPublications", "MiniAppReviewActions", "MiniAppReviewRequests", "MiniAppVersions";
            ALTER TABLE "MiniAppOAuthClients" DROP CONSTRAINT IF EXISTS "AK_MiniAppOAuthClients_Id_MiniAppId";
            ALTER TABLE "MiniApps" DROP CONSTRAINT IF EXISTS "AK_MiniApps_Id_DeveloperAccountId";
            DROP INDEX IF EXISTS "IX_MiniAppDomainVerifications_MiniAppId_Hostname";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_MiniAppDomainVerifications_MiniAppId" ON "MiniAppDomainVerifications" ("MiniAppId") WHERE "RevokedAt" IS NULL;
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
            """);
        var baseline = db.Database.GetMigrations().Single(x => x.EndsWith("_AddSandboxExecutorLaunchBinding"));
        var migrator = db.GetService<IMigrator>();
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(baseline, current));
        return (previous, current, migrator);
    }

    internal static async Task Denied(Viora.Infrastructure.Persistence.AppDbContext db, string sql)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        await transaction.RollbackAsync();
    }
}
