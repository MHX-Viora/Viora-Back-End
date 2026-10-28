using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionCatalogMigrationTests
{
    [Fact]
    public void CatalogMigrationIsAdditiveAndNeverCreatesApprovalOrPublication()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused_offline_model").Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var current = Assert.Single(migrations, x => x.EndsWith("_AddMiniAppProductionCatalog"));
        var previous = migrations.Last(x => string.CompareOrdinal(x, current) < 0);
        var sql = db.GetService<IMigrator>().GenerateScript(previous, current);
        Assert.Contains("CREATE TABLE \"MiniAppCatalogPublications\"", sql);
        Assert.Contains("CREATE TRIGGER", sql);
        Assert.DoesNotContain("INSERT INTO \"MiniAppPublications\"", sql);
        Assert.DoesNotContain("UPDATE \"MiniAppReviewRequests\"", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }

    [Fact]
    public async Task PostgreSqlRejectsProjectionTamperingAndPreservesAuthorityAcrossUpDownUp()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options);
        var current = db.Database.GetMigrations().Single(x => x.EndsWith("_AddMiniAppProductionCatalog"));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        await migrator.MigrateAsync(current);
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        var forged = MiniAppProductionCatalogPersistenceTests.Projection(target); forged.Name = "Unreviewed metadata";
        f.Db.Add(forged);
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(insert.InnerException).SqlState);
        f.Db.ChangeTracker.Clear();
        f.Db.Add(MiniAppProductionCatalogPersistenceTests.Projection(target)); await f.Db.SaveChangesAsync();
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppCatalogPublications\" SET \"Name\"='forged'");
        await MiniAppReviewMigrationTests.Denied(f.Db, "DELETE FROM \"MiniAppCatalogPublications\"");
        await migrator.MigrateAsync(previous);
        Assert.Single(await f.Db.MiniAppPublications.ToArrayAsync());
        Assert.Single(await f.Db.MiniAppReviewRequests.ToArrayAsync());
        Assert.Single(await f.Db.DeveloperSessions.ToArrayAsync());
        await migrator.MigrateAsync(current);
        Assert.Empty(await f.Db.Set<MiniAppCatalogPublication>().ToArrayAsync());
        Assert.Single(await f.Db.MiniAppPublications.ToArrayAsync());
    }
}
