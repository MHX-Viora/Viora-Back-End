using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

public sealed class Stage10DatabaseRecoveryTests(ITestOutputHelper output)
{
    [Fact]
    public void CurrentModelMatchesMigrationSnapshotWithoutConnecting()
    {
        using var db = Context("Host=127.0.0.1;Database=stage10_offline_model");
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task OldDeveloperDataSurvivesFullUpgradeAndIndependentDumpRestore()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var source = Context(pg.ConnectionString);
        var checkpoint = source.Database.GetMigrations().Single(x => x.EndsWith("_AddIndependentDeveloperAccounts"));
        await source.GetService<IMigrator>().MigrateAsync(checkpoint);
        var developer = new DeveloperAccount
        {
            FullName = "Stage 10 recovery fixture", EmailNormalized = "RECOVERY@EXAMPLE.TEST",
            TermsAcceptedAt = DateTime.UtcNow, Status = DeveloperStatus.Active
        };
        source.Add(developer);
        await source.SaveChangesAsync();
        await source.Database.MigrateAsync();
        Assert.Empty(await source.Database.GetPendingMigrationsAsync());
        Assert.Equal(developer.EmailNormalized, (await source.DeveloperAccounts.AsNoTracking().SingleAsync()).EmailNormalized);
        var expected = await Schema(source);
        var target = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = "stage10_restore" };
        await source.Database.ExecuteSqlRawAsync("CREATE DATABASE stage10_restore");
        // Both cluster and archive paths are generated here; no application DB input.
        var archive = Path.Combine(Path.GetTempPath(), "ankt-stage10-recovery-" + Guid.NewGuid().ToString("N") + ".dump");
        try
        {
            var timer = Stopwatch.StartNew();
            await Tool("pg_dump", pg.ConnectionString, "--format=custom", "--file", archive);
            var backupMs = timer.ElapsedMilliseconds;
            timer.Restart();
            await Tool("pg_restore", target.ConnectionString, "--exit-on-error", "--no-owner", archive);
            var restoreMs = timer.ElapsedMilliseconds;
            await using var restored = Context(target.ConnectionString);
            var actual = await Schema(restored);
            Assert.True(expected.SequenceEqual(actual), "Schema difference: " +
                string.Join("; ", expected.Except(actual).Take(1).Select(x => "missing " + x)
                    .Concat(actual.Except(expected).Take(1).Select(x => "extra " + x))));
            Assert.Equal(await source.Database.GetAppliedMigrationsAsync(), await restored.Database.GetAppliedMigrationsAsync());
            var recovered = await restored.DeveloperAccounts.AsNoTracking().SingleAsync();
            Assert.Equal(developer.Id, recovered.Id);
            Assert.Equal(developer.EmailNormalized, recovered.EmailNormalized);
            Assert.Equal(developer.SecurityVersion, recovered.SecurityVersion);
            // Verify restored foreign keys enforce integrity, not just matching table counts.
            restored.DeveloperSessions.Add(new DeveloperSession
            {
                DeveloperAccountId = Guid.NewGuid(), TokenHash = "stage10-test-only",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5), AbsoluteExpiresAt = DateTime.UtcNow.AddMinutes(10),
                LastUsedAt = DateTime.UtcNow
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => restored.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            output.WriteLine($"Isolated PG full chain: {expected.Length} schema entries; backup {backupMs} ms; restore {restoreMs} ms. Synthetic developer only; not production RPO/RTO.");
        }
        finally { if (File.Exists(archive)) File.Delete(archive); }
    }

    private static AppDbContext Context(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    private static async Task<string[]> Schema(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT 'column:' || table_name || ':' || column_name || ':' || data_type || ':' || is_nullable
            FROM information_schema.columns WHERE table_schema = 'public'
            UNION ALL SELECT 'index:' || indexname || ':' || indexdef FROM pg_indexes WHERE schemaname = 'public'
            UNION ALL SELECT 'constraint:' || c.conname || ':' || pg_get_constraintdef(c.oid)
            FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'public' AND c.contype <> 'c'
            UNION ALL SELECT 'trigger:' || t.tgname || ':' || pg_get_triggerdef(t.oid)
            FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace
            WHERE n.nspname='public' AND NOT t.tgisinternal
            ORDER BY 1
            """;
        var entries = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) entries.Add(reader.GetString(0));
        command.CommandText = """
            SELECT r.relname, c.conname, pg_get_constraintdef(c.oid)
            FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid
            JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname='public' AND c.contype='c'
            """;
        var checks = new List<(string Table, string Name, string Definition)>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) checks.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        using var quoting = new NpgsqlCommandBuilder();
        foreach (var check in checks)
        {
            // PG18 reparses array casts/BETWEEN when restoring DDL. Canonicalize both
            // sides through the same parser; do not discard or regex-strip expressions.
            command.CommandText = $"CREATE TEMP TABLE stage10_check (LIKE public.{quoting.QuoteIdentifier(check.Table)}); " +
                $"ALTER TABLE stage10_check ADD CONSTRAINT canonical {check.Definition}; " +
                "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid='pg_temp.stage10_check'::regclass AND conname='canonical'";
            entries.Add("constraint:" + check.Name + ":" + (string)(await command.ExecuteScalarAsync())!);
            command.CommandText = "DROP TABLE pg_temp.stage10_check";
            await command.ExecuteNonQueryAsync();
        }
        Assert.NotEmpty(entries);
        return entries.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task Tool(string name, string connection, params string[] arguments)
    {
        var database = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1", database.Host);
        Assert.Equal("ankt_test", database.Username);
        Assert.Contains(database.Database, new[] { "postgres", "stage10_restore" });
        var binaries = Environment.GetEnvironmentVariable("ANKT_TEST_PG_BIN") ?? "C:/Program Files/PostgreSQL/18/bin";
        var start = new ProcessStartInfo(Path.Combine(binaries, name + (OperatingSystem.IsWindows() ? ".exe" : "")))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Npgsql's semicolon syntax is not a libpq connection string.
        foreach (var argument in new[] { "--host", database.Host, "--port", database.Port.ToString(),
            "--username", database.Username, "--dbname", database.Database, "--no-password" }.Concat(arguments))
            start.ArgumentList.Add(argument!);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start owned recovery tool.");
        // Consume without logging: tool diagnostics can contain connection details.
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        await Task.WhenAll(stdout, stderr);
        Assert.True(process.ExitCode == 0, $"Owned recovery {name} failed with exit code {process.ExitCode}.");
    }
}
