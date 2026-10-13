using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Migrations;
using Xunit;

public sealed class MiniAppSchemaConsolidationTests
{
    [MiniAppPostgresFact]
    public async Task MigrationPreservesReviewFieldsAndCutoffsAndRollbackRestoresThem()
    {
        await using var f = await Fixture.Create();
        await f.SeedLegacy();
        await f.Apply(up: true);
        f.Db.ChangeTracker.Clear();
        var review = await f.Db.MiniAppAuditLogs.SingleAsync(x => x.Id == f.ReviewId);
        Assert.Equal(MiniAppAuditLog.DeveloperReviewEvent, review.EventType);
        Assert.Equal(f.Developer.Id, review.DeveloperId);
        Assert.Equal(f.Owner.Id, review.ActorAccountId);
        Assert.Equal(0, review.ReviewVersion);
        Assert.Equal("Suspended", review.ReviewPreviousStatus);
        Assert.Equal("Revoked", review.ReviewStatus);
        Assert.Equal("Public reason", review.ReviewReason);
        Assert.Equal("Private evidence", review.ReviewInternalNotes);
        Assert.Equal(f.Cutoff, review.CreatedAt);
        Assert.Equal(f.Cutoff, (await f.Db.Accounts.SingleAsync(x => x.Id == f.Owner.Id)).MiniAppHistoryClearedBefore);
        Assert.Null((await f.Db.Accounts.SingleAsync(x => x.Id == f.Other.Id)).MiniAppHistoryClearedBefore);
        Assert.Equal(2, await f.Db.MiniAppAuditLogs.CountAsync());
        Assert.Equal(0L, await f.Scalar("SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name IN ('DeveloperReviewHistory', 'MiniAppHistoryStates', 'MiniAppLaunchCodes')"));
        await f.Apply(up: false);
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"DeveloperReviewHistory\" WHERE \"Version\" = 0 AND \"Status\" = 'Revoked' AND \"InternalNotes\" = 'Private evidence'"));
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"MiniAppHistoryStates\" WHERE \"ClearedBefore\" = TIMESTAMPTZ '2026-10-01 12:00:00+00'"));
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"MiniAppAuditLogs\""));
        await f.Apply(up: true);
        Assert.Equal(2L, await f.Scalar("SELECT count(*) FROM \"MiniAppAuditLogs\""));
    }

    [MiniAppPostgresFact]
    public async Task CollidingReviewIdAbortsWithoutDroppingSourceData()
    {
        await using var f = await Fixture.Create();
        await f.SeedLegacy();
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppAuditLogs\" SET \"Id\" = {f.ReviewId}");
        await Assert.ThrowsAsync<PostgresException>(() => f.Apply(up: true));
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"DeveloperReviewHistory\""));
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"MiniAppHistoryStates\""));
    }

    [MiniAppPostgresFact]
    public async Task UnexpiredUnusedLegacyCodeAbortsMigration()
    {
        await using var f = await Fixture.Create();
        await f.SeedLegacy();
        var app = new MiniApp { DeveloperId = f.Developer.Id, Name = "Fixture", Slug = "fixture", WebUrl = "https://example.com", CallbackUrl = "", ClientId = "fixture", ClientSecretHash = "fixture-only" };
        f.Db.Add(app); await f.Db.SaveChangesAsync();
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MiniAppLaunchCodes\" (\"Id\", \"AccountId\", \"MiniAppId\", \"CodeHash\", \"CreatedAt\", \"ExpiresAt\") VALUES ({Guid.NewGuid()}, {f.Owner.Id}, {app.Id}, {'a'.ToString().PadRight(64, 'a')}, {DateTime.UtcNow}, {DateTime.UtcNow.AddHours(1)})");
        await Assert.ThrowsAsync<PostgresException>(() => f.Apply(up: true));
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"MiniAppLaunchCodes\""));
        Assert.Equal(1L, await f.Scalar("SELECT count(*) FROM \"DeveloperReviewHistory\""));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Db = null!;
        private NpgsqlConnection admin = null!;
        private string schema = "consolidation_test_" + Guid.NewGuid().ToString("N");
        public Account Owner = new() { Email = "owner@schema.test" };
        public Account Other = new() { Email = "other@schema.test" };
        public Developer Developer = null!;
        public Guid ReviewId = Guid.NewGuid();
        public DateTime Cutoff = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        public static async Task<Fixture> Create()
        {
            var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VIORA_MINI_APP_TEST_DB"));
            if (!(builder.Database ?? "").Contains("test", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Dedicated test DB required.");
            var f = new Fixture();
            f.admin = new(builder.ConnectionString); await f.admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{f.schema}\"", f.admin)) await create.ExecuteNonQueryAsync();
            builder.SearchPath = f.schema; builder.Pooling = false;
            f.Db = new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options);
            await f.Db.Database.ExecuteSqlRawAsync(f.Db.Database.GenerateCreateScript());
            f.Developer = new() { AccountId = f.Owner.Id, Name = "Fixture", Email = f.Owner.Email!, Status = DeveloperStatus.Active };
            f.Db.AddRange(f.Owner, f.Other, f.Developer, new MiniAppAuditLog { DeveloperId = f.Developer.Id, ActorAccountId = f.Owner.Id, Action = "DeveloperSubmitted" });
            await f.Db.SaveChangesAsync();
            await f.Apply(up: false);
            return f;
        }

        public Task SeedLegacy() => Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DeveloperReviewHistory\" (\"Id\", \"DeveloperId\", \"ReviewerAccountId\", \"CreatedAt\", \"Version\", \"PreviousStatus\", \"Status\", \"Reason\", \"InternalNotes\") VALUES ({ReviewId}, {Developer.Id}, {Owner.Id}, {Cutoff}, 0, 'Suspended', 'Revoked', 'Public reason', 'Private evidence'); INSERT INTO \"MiniAppHistoryStates\" (\"Id\", \"AccountId\", \"ClearedBefore\", \"CreatedAt\") VALUES ({Guid.NewGuid()}, {Owner.Id}, {Cutoff}, {Cutoff})");

        public async Task Apply(bool up)
        {
            var migration = new ConsolidateMiniAppSchema();
            var commands = Db.GetService<IMigrationsSqlGenerator>().Generate(up ? migration.UpOperations : migration.DownOperations, Db.Model);
            await using var transaction = await Db.Database.BeginTransactionAsync();
            foreach (var command in commands) await Db.Database.ExecuteSqlRawAsync(command.CommandText);
            await transaction.CommitAsync();
        }

        public async Task<long> Scalar(string sql)
        {
            await Db.Database.OpenConnectionAsync();
            await using var cmd = Db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = sql;
            return (long)(await cmd.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin); await drop.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
        }
    }
}
