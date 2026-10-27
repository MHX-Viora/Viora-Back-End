using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class LegacySsoArchiveMigrationTests
{
    private static readonly string[] RetiredTables =
    [
        "OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens",
        "AnktSsoSessions", "SsoLoginTransactions", "MiniAppPairwiseSubjects", "MiniAppUserConsents",
        "MiniAppAccountLinks", "SsoAccountLinkChallenges", "SsoAccountLinkAudits",
        "SandboxTestIdentities", "SandboxIdentitySessions"
    ];

    [Fact]
    public void RuntimeModelHasNoLegacySsoStoresAndMigrationDoesNotDeleteData()
    {
        using var db = Context("Host=127.0.0.1;Database=unused_offline_model");
        Assert.DoesNotContain(db.Model.GetEntityTypes(), x => RetiredTables.Contains(x.GetTableName()));
        var current = Assert.Single(db.Database.GetMigrations(), x => x.EndsWith("_RetireMiniAppLegacySsoStores"));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        var sql = db.GetService<IMigrator>().GenerateScript(previous, current);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
        Assert.Contains("SET SCHEMA legacy_sso", sql);
    }

    [Fact]
    public async Task ArchiveRoundTripPreservesLegacyRowsAndActiveTablesOnIsolatedPostgres()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var db = Context(pg.ConnectionString);
        var current = Assert.Single(db.Database.GetMigrations(), x => x.EndsWith("_RetireMiniAppLegacySsoStores"));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"OpenIddictScopes\" (\"Id\", \"Name\") VALUES ('archive-sentinel', 'profile')");
        await migrator.MigrateAsync(current);
        await Check("legacy_sso");
        await migrator.MigrateAsync(previous);
        await Check("public");
        await migrator.MigrateAsync(current);
        await Check("legacy_sso");

        async Task Check(string schema)
        {
            await using var connection = new NpgsqlConnection(pg.ConnectionString);
            await connection.OpenAsync();
            foreach (var table in RetiredTables)
            {
                await using var exists = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
                exists.Parameters.AddWithValue("name", $"{schema}.\"{table}\"");
                Assert.Equal(true, await exists.ExecuteScalarAsync());
            }
            await using var count = new NpgsqlCommand($"SELECT COUNT(*) FROM {schema}.\"OpenIddictScopes\" WHERE \"Id\"='archive-sentinel'", connection);
            Assert.Equal(1L, await count.ExecuteScalarAsync());
            foreach (var table in new[] { "Accounts", "Users", "MiniApps", "DeveloperSessions", "MiniAppBridgeSessions", "SandboxIdentityTemplates", "MiniAppOAuthClients" })
            {
                await using var active = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
                active.Parameters.AddWithValue("name", $"public.\"{table}\"");
                Assert.Equal(true, await active.ExecuteScalarAsync());
            }
        }
    }

    private static AppDbContext Context(string connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
}
