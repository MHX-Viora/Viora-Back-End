using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenIddict.EntityFrameworkCore.Models;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoPersistenceTests
{
    [Fact]
    public async Task PostgreSqlMigrationRollbackTransactionsAndOptimisticConcurrencyAreReal()
    {
        await using var postgres = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();
        var previous = "20261016022554_AddMiniAppDomainVerification";
        var migration = db.Database.GetMigrations().Single(x => x.EndsWith("_AddOpenIddictStores"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); CREATE TABLE untouched (id integer PRIMARY KEY);");
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, migration));
        db.Set<OpenIddictEntityFrameworkCoreApplication>().Add(new() { ClientId = "test-client", DisplayName = "Before" });
        await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Set<OpenIddictEntityFrameworkCoreApplication>().Add(new() { ClientId = "rolled-back" });
            await db.SaveChangesAsync(); await transaction.RollbackAsync();
        }
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        Assert.False(await first.Set<OpenIddictEntityFrameworkCoreApplication>().AnyAsync(x => x.ClientId == "rolled-back"));
        var a = await first.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync();
        var b = await second.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync();
        a.DisplayName = "First"; a.ConcurrencyToken = Guid.NewGuid().ToString(); await first.SaveChangesAsync();
        b.DisplayName = "Second"; b.ConcurrencyToken = Guid.NewGuid().ToString();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using (var duplicate = new AppDbContext(options))
        {
            duplicate.Set<OpenIddictEntityFrameworkCoreApplication>().Add(new() { ClientId = "test-client" });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(migration, previous));
        await db.Database.ExecuteSqlRawAsync("INSERT INTO untouched VALUES (1)");
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, migration));
        await using var clean = new AppDbContext(options);
        Assert.Empty(await clean.Set<OpenIddictEntityFrameworkCoreApplication>().ToListAsync());
    }
    [Fact]
    public void SharedContextIncludesStandardOidcStoresWithConcurrencyAndUniqueClientId()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused_model_only;Username=unused").Options);
        var entities = db.Model.GetEntityTypes().Where(x => x.GetTableName()?.StartsWith("OpenIddict") == true).ToArray();
        Assert.Equal(4, entities.Length);
        foreach (var table in new[] { "OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens" })
        {
            var entity = Assert.Single(entities, x => x.GetTableName() == table);
            Assert.True(entity.FindProperty("ConcurrencyToken")!.IsConcurrencyToken);
        }
        var application = entities.Single(x => x.GetTableName() == "OpenIddictApplications");
        Assert.Contains(application.GetIndexes(), x => x.IsUnique && x.Properties.SingleOrDefault()?.Name == "ClientId");
    }
}
