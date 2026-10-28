using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionHostMigrationTests
{
    [Fact]
    public async Task PostgreSqlHostStoragePinsAuthorityAndPreventsRetargetOrGrantResurrection()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options);
        var current = Assert.Single(db.Database.GetMigrations(), x => x.EndsWith("_AddMiniAppProductionHost"));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(current);
        // Current publication commands revoke bridge sessions; install that independent runtime
        // dependency without changing this historical migration's history under test.
        var bridgeCommands = db.GetService<IMigrationsSqlGenerator>().Generate(
            new Viora.Infrastructure.Persistence.Migrations.AddMiniAppAuthBridge().UpOperations);
        foreach (var command in bridgeCommands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var readiness = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(readiness), default);
        var target = await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default);
        var session = MiniAppProductionHostPersistenceTests.Session(target);
        session.OperationalRevision = await f.Db.MiniAppOperationalStates.Select(x => x.ConcurrencyToken).SingleAsync();
        f.Db.Add(session); await f.Db.SaveChangesAsync();
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppProductionHostSessions\" SET \"SnapshotHash\"=repeat('f',64)");
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppProductionHostSessions\" SET \"AccountId\"=gen_random_uuid(), \"SourceSocialSessionId\"=gen_random_uuid()");
        var grant = MiniAppProductionHostPersistenceTests.Grant(session); f.Db.Add(grant); await f.Db.SaveChangesAsync();
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppProductionLaunchGrants\" SET \"Audience\"='https://other.example.com'");
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE \"MiniAppProductionLaunchGrants\" SET \"ConsumedAt\"=clock_timestamp(), \"ReceiptProtected\"=NULL");
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppProductionLaunchGrants\" SET \"ConsumedAt\"=NULL, \"ReceiptProtected\"='replayed'");
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE \"MiniAppProductionHostSessions\" SET \"ClosedAt\"=clock_timestamp()");
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppProductionHostSessions\" SET \"ClosedAt\"=NULL");
        await migrator.MigrateAsync(previous);
        Assert.Single(await f.Db.MiniAppPublications.ToArrayAsync());
        Assert.Single(await f.Db.DeveloperSessions.ToArrayAsync());
        await migrator.MigrateAsync(current);
        Assert.Empty(await f.Db.Set<MiniAppProductionHostSession>().ToArrayAsync());
        Assert.Single(await f.Db.MiniAppReviewRequests.ToArrayAsync());
    }
}
