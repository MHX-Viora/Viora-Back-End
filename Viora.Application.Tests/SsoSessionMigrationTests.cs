using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoSessionMigrationTests
{
    [Fact]
    public async Task SessionMigrationEnforcesHashUniquenessOwnershipAndAtomicRevocationOnPostgreSql()
    {
        await using var cluster = await IsolatedPostgres.Start();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cluster.ConnectionString).Options);
        var previous = db.Database.GetMigrations().Single(x => x.EndsWith("_AddSsoClientEnvironments"));
        var current = db.Database.GetMigrations().Single(x => x.EndsWith("_AddAnktSsoSessions"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); CREATE TABLE \"Accounts\" (\"Id\" uuid PRIMARY KEY);");
        var account = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Accounts\" VALUES ({account})");
        var migrator = db.GetService<IMigrator>();
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        db.AnktSsoSessions.Add(new AnktSsoSession { AccountId = account, SocialSessionId = Guid.NewGuid(), TokenHash = new string('A',64), ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.AnktSsoSessions.Where(x => x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTime.UtcNow)));
        Assert.Equal(0, await db.AnktSsoSessions.Where(x => x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTime.UtcNow)));
        db.AnktSsoSessions.Add(new AnktSsoSession { AccountId = account, TokenHash = new string('A',64), ExpiresAt = DateTime.UtcNow.AddHours(1) });
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)unique.InnerException!).SqlState);
        db.ChangeTracker.Clear();
        db.AnktSsoSessions.Add(new AnktSsoSession { AccountId = Guid.NewGuid(), TokenHash = new string('B',64), ExpiresAt = DateTime.UtcNow.AddHours(1) });
        var ownership = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)ownership.InnerException!).SqlState);
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"Accounts\"").SingleAsync());
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
    }
}
