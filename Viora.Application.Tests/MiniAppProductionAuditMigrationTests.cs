using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppProductionAuditMigrationTests
{
    [Fact]
    public async Task PostgreSqlAuditRejectsTamperingAndReplayAndDowngradePreservesHostAndPublication()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options);
        var current = Assert.Single(db.Database.GetMigrations(), x => x.EndsWith("_AddMiniAppProductionAudit"));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, current) < 0);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(current);
        // Current publication commands revoke bridge sessions; install that independent runtime
        // dependency without changing this historical migration's history under test.
        var bridgeCommands = db.GetService<IMigrationsSqlGenerator>().Generate(
            new Viora.Infrastructure.Persistence.Migrations.AddMiniAppAuthBridge().UpOperations);
        foreach (var command in bridgeCommands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var approved = await MiniAppPublishingReadinessTests.Approved(f);
        var ready = (await MiniAppReviewReadinessTests.Service(f).PublicationAsync(f.Actor.Id, f.Source.Id, f.App.AppId, approved.VersionId, default)).Readiness;
        await MiniAppPublishingTests.Service(f).PublishAsync(f.Actor.Id, f.Source.Id, f.App.AppId, MiniAppPublishingTests.Command(ready), default);
        var session = MiniAppProductionHostPersistenceTests.Session(await MiniAppProductionEligibility.ReadAsync(f.Db, f.App.Id, f.Clock.Now.UtcDateTime, default));
        session.OperationalRevision = await f.Db.MiniAppOperationalStates.Select(x => x.ConcurrencyToken).SingleAsync();
        f.Db.Add(session); await f.Db.SaveChangesAsync();
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        { new MiniAppProductionAudit(f.Db).Append(session, MiniAppProductionEventCode.LaunchGranted); await f.Db.SaveChangesAsync(); await tx.CommitAsync(); }
        f.Db.Add(MiniAppProductionEventPersistenceTests.Event(session));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        f.Db.ChangeTracker.Clear();
        await MiniAppReviewMigrationTests.Denied(f.Db, "UPDATE \"MiniAppProductionRuntimeEvents\" SET \"Code\"=3");
        await MiniAppReviewMigrationTests.Denied(f.Db, "DELETE FROM \"MiniAppProductionRuntimeEvents\"");
        await migrator.MigrateAsync(previous);
        Assert.Single(await f.Db.Set<MiniAppProductionHostSession>().ToArrayAsync());
        Assert.Single(await f.Db.MiniAppPublications.ToArrayAsync());
        await migrator.MigrateAsync(current);
        Assert.Empty(await f.Db.Set<MiniAppProductionRuntimeEvent>().ToArrayAsync());
        Assert.Single(await f.Db.DeveloperSessions.ToArrayAsync());
    }
}
