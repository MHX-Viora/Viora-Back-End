using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Xunit;

public sealed class MiniAppVersionMigrationTests
{
    [Fact]
    public async Task PostgreSqlBoundsSnapshotBytesAndRequiresAnObjectAndLowercaseHash()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await MiniAppReviewMigrationTests.Prepare(f.Db, "AddMiniAppVersions");
        var version = MiniAppVersionPersistenceTests.Version(f.App);
        f.Db.Add(version); await f.Db.SaveChangesAsync();
        var oversized = "{\"description\":\"" + new string('界', 30000) + "\"}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => f.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"MiniAppVersions\" SET \"SnapshotJson\"={oversized} WHERE \"Id\"={version.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        error = await Assert.ThrowsAsync<PostgresException>(() => f.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"MiniAppVersions\" SET \"SnapshotHash\"={new string('Z', 64)} WHERE \"Id\"={version.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        error = await Assert.ThrowsAsync<PostgresException>(() => f.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"MiniAppVersions\" SET \"SnapshotJson\"={"[]"} WHERE \"Id\"={version.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task VersionsRoundTripAndSubmittedSnapshotsRejectOrdinarySqlTampering()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var current = f.Db.Database.GetMigrations().Single(x => x.EndsWith("_AddMiniAppVersions"));
        var previous = f.Db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        var migrator = f.Db.GetService<IMigrator>();
        // Nonempty fixture metadata, not usable credentials: prove additive migration preservation.
        f.Client.ClientType = "Confidential";
        var account = new Account { Email = "stage7-preservation@example.test" };
        var social = new RefreshToken { Account = account, SessionId = Guid.NewGuid(), TokenHash = "stage7-social-fixture", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        f.Db.Add(social); await f.Db.SaveChangesAsync();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL)");
        // EnsureCreated installs model tables, not migration-only triggers. Start at the predecessor.
        await f.Db.Database.ExecuteSqlRawAsync("""
            DROP TABLE IF EXISTS "MiniAppProductionRuntimeEvents", "MiniAppProductionLaunchGrants", "MiniAppProductionHostSessions", "MiniAppCatalogPublications", "MiniAppReviewNotificationDeliveries", "MiniAppReviewNotifications",
                "MiniAppOperationalStates", "MiniAppPublications", "MiniAppReviewActions", "MiniAppReviewRequests", "MiniAppVersions";
            """);
        var up = migrator.GenerateScript(previous, current);
        Assert.DoesNotContain("DROP TABLE", up);
        await f.Db.Database.ExecuteSqlRawAsync(up);
        var version = MiniAppVersionPersistenceTests.Version(f.App);
        f.Db.Add(version);
        await f.Db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppVersions\" SET \"State\"=1, \"SubmittedAt\"={now} WHERE \"Id\"={version.Id}");
        foreach (var field in new[] { "SnapshotJson", "SnapshotHash", "VersionCode", "MiniAppId", "CreatedByDeveloperId", "VersionName", "SubmittedAt", "State", "LogoUploadId" })
        {
            var expression = field switch
            {
                "SnapshotJson" => "jsonb_build_object('tampered',true)::text", "SnapshotHash" => "repeat('b',64)",
                "VersionCode" => "2", "VersionName" => "'tampered'", "SubmittedAt" => "now() + interval '1 second'",
                "State" => "0", _ => "gen_random_uuid()"
            };
            // Field and expression come exclusively from the fixed test catalog above.
            var sql = $"UPDATE \"MiniAppVersions\" SET \"{field}\"={expression} WHERE \"Id\"='{version.Id}'";
            var error = await Assert.ThrowsAsync<PostgresException>(() => f.Db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        var deleted = await Assert.ThrowsAsync<PostgresException>(() => f.Db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"MiniAppVersions\" WHERE \"Id\"={version.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, deleted.SqlState);
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppVersions\" SET \"State\"=2 WHERE \"Id\"={version.Id}");
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Single(await f.Db.MiniAppOAuthClients.AsNoTracking().ToListAsync());
        Assert.Single(await f.Db.DeveloperSessions.AsNoTracking().ToListAsync());
        Assert.Single(await f.Db.MiniAppDomainVerifications.AsNoTracking().ToListAsync());
        Assert.Equal(social.TokenHash, await f.Db.RefreshTokens.Select(x => x.TokenHash).SingleAsync());
        Assert.Single(await f.Db.Accounts.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(up);
        Assert.Empty(await f.Db.MiniAppVersions.AsNoTracking().ToListAsync());
    }
}
