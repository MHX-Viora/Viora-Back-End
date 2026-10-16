using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoClientEnvironmentMigrationTests
{
    [Fact]
    public async Task ExistingClientsRemainDisabledSandboxAndNewEnvironmentsHaveSeparateUniqueBindings()
    {
        await using var cluster = await IsolatedPostgres.Start();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cluster.ConnectionString).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
            CREATE TABLE "MiniAppOAuthClients" ("Id" uuid PRIMARY KEY, "MiniAppId" uuid NOT NULL);
            CREATE UNIQUE INDEX "IX_MiniAppOAuthClients_MiniAppId" ON "MiniAppOAuthClients" ("MiniAppId");
            CREATE TABLE "MiniAppRedirectUris" ("MiniAppId" uuid NOT NULL, "RedirectUri" text NOT NULL);
            CREATE UNIQUE INDEX "IX_MiniAppRedirectUris_MiniAppId_RedirectUri" ON "MiniAppRedirectUris" ("MiniAppId", "RedirectUri");
            INSERT INTO "MiniAppOAuthClients" VALUES ('11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222');
            """);
        var previous = db.Database.GetMigrations().Single(x => x.EndsWith("_AddOpenIddictStores"));
        var current = db.Database.GetMigrations().Single(x => x.EndsWith("_AddSsoClientEnvironments"));
        var migrator = db.GetService<IMigrator>();
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"MiniAppOAuthClients\" WHERE \"Environment\"=0 AND NOT \"SandboxEnabled\" AND \"AllowedScopes\"=''").SingleAsync());
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "MiniAppOAuthClients" ("Id","MiniAppId","Environment") VALUES
            ('33333333-3333-3333-3333-333333333333','22222222-2222-2222-2222-222222222222',1);
            INSERT INTO "MiniAppRedirectUris" VALUES ('22222222-2222-2222-2222-222222222222','https://partner.example/callback',0),
            ('22222222-2222-2222-2222-222222222222','https://partner.example/callback',1);
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "MiniAppOAuthClients" ("Id","MiniAppId","Environment") VALUES
            ('44444444-4444-4444-4444-444444444444','22222222-2222-2222-2222-222222222222',1)
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        // Downgrade requires operator removal of duplicate app/environment rows; never silently delete them.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"MiniAppOAuthClients\" WHERE \"Environment\"=1; DELETE FROM \"MiniAppRedirectUris\" WHERE \"Environment\"=1;");
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"MiniAppOAuthClients\"").SingleAsync());
    }
}
