using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using Xunit;

public sealed class MiniAppSandboxEvidencePostgresTests
{
    [Fact]
    public async Task AdditiveEvidenceMigrationRoundTripsAndPreservesSessionsClientsAndConsent()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);var account=new Account {Email="sandbox-evidence-pg@example.test"};
        f.Db.Accounts.Add(account);
        await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        var previous=f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_AddMiniAppSandboxSessions"));
        var current=f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_AddMiniAppSandboxEvidence"));
        // Isolate this predecessor migration from the later executor grant FK in EnsureCreated.
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE \"MiniAppSandboxLaunchGrants\" DROP CONSTRAINT \"FK_MiniAppSandboxLaunchGrants_MiniAppSandboxTestRuns_ExecutorR~\";");
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE \"MiniAppSandboxProtocolEvidence\"; DROP TABLE \"MiniAppSandboxDiagnostics\"; DROP TABLE \"MiniAppSandboxTestResults\"; DROP TABLE \"MiniAppSandboxTestRuns\"; ALTER TABLE \"MiniAppSandboxSessions\" DROP CONSTRAINT \"AK_MiniAppSandboxSessions_Id_ConfigurationFingerprint\"; CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
        var migrator=f.Db.GetService<IMigrator>();var up=migrator.GenerateScript(previous,current);
        Assert.DoesNotContain("DROP TABLE",up);Assert.DoesNotContain("DELETE FROM",up);Assert.DoesNotContain("UPDATE ",up);
        await f.Db.Database.ExecuteSqlRawAsync(up);
        var run=MiniAppSandboxEvidencePersistenceTests.Run(session.Id,session.ConfigurationFingerprint);f.Db.Add(run);await f.Db.SaveChangesAsync();
        var result=MiniAppSandboxEvidencePersistenceTests.Result(run.Id);result.TestKey=SandboxCheckKey.Token;f.Db.Add(result);await f.Db.SaveChangesAsync();
        f.Db.Add(new MiniAppSandboxDiagnostic {SandboxSessionId=session.Id,TestRunId=run.Id,Code=SandboxMessageCode.RunQueued});
        f.Db.Add(new MiniAppSandboxProtocolEvidence {TestRunId=run.Id,TestKey=SandboxCheckKey.Token,Stage=SandboxProtocolStage.Token,Outcome=SandboxProtocolOutcome.TokenIssued,EventKey=new string('a',64),TokenId="internal-row-id"});await f.Db.SaveChangesAsync();
        var denied=await Assert.ThrowsAsync<PostgresException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppSandboxTestResults\" SET \"Status\"=0, \"EvidenceSource\"=2, \"EvidenceAt\"={DateTime.UtcNow} WHERE \"Id\"={result.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation,denied.SqlState);
        denied=await Assert.ThrowsAsync<PostgresException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppSandboxProtocolEvidence\" SET \"TokenId\"={new string('x',101)}"));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation,denied.SqlState);
        await using(var tx=await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.MiniAppSandboxDiagnostics.ExecuteDeleteAsync();await tx.RollbackAsync();
        }
        Assert.Single(await f.Db.MiniAppSandboxDiagnostics.AsNoTracking().ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous));
        Assert.Single(await f.Db.MiniAppSandboxSessions.AsNoTracking().ToListAsync());
        Assert.Single(await f.Db.MiniAppOAuthClients.AsNoTracking().ToListAsync());Assert.Single(await f.Db.Accounts.AsNoTracking().ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(up);Assert.Empty(await f.Db.MiniAppSandboxTestRuns.AsNoTracking().ToListAsync());
        Assert.Single(await f.Db.MiniAppSandboxSessions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostgreSqlAllowsExactlyOneConcurrentActiveRun()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var ready=0;var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Insert()
        {
            await using var db=new AppDbContext(options);db.Add(MiniAppSandboxEvidencePersistenceTests.Run(session.Id,session.ConfigurationFingerprint));
            if(Interlocked.Increment(ref ready)==2)start.SetResult();await start.Task;
            try{await db.SaveChangesAsync();return true;}
            catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation}){return false;}
        }
        Assert.Single(await Task.WhenAll(Insert(),Insert()),x=>x);Assert.Single(await f.Db.MiniAppSandboxTestRuns.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DiagnosticCounterMigrationHasSafeDefaultBoundsAndRollback()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var current=f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_AddMiniAppSandboxDiagnosticCounter"));
        var previous=f.Db.Database.GetMigrations().Single(x=>x.EndsWith("_AddMiniAppSandboxEvidence"));var migrator=f.Db.GetService<IMigrator>();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous));
        Assert.Equal(session.Id,await f.Db.MiniAppSandboxSessions.Select(x=>x.Id).SingleAsync());
        var up=migrator.GenerateScript(previous,current);Assert.DoesNotContain("DROP ",up);Assert.DoesNotContain("UPDATE ",up);
        await f.Db.Database.ExecuteSqlRawAsync(up);
        Assert.Equal(0,await f.Db.MiniAppSandboxSessions.Select(x=>x.DroppedDiagnosticCount).SingleAsync());
        var denied=await Assert.ThrowsAsync<PostgresException>(()=>f.Db.Database.ExecuteSqlRawAsync("UPDATE \"MiniAppSandboxSessions\" SET \"DroppedDiagnosticCount\"=1000001"));Assert.Equal(PostgresErrorCodes.CheckViolation,denied.SqlState);
        await using(var tx=await f.Db.Database.BeginTransactionAsync())
        {
            await f.Db.Database.ExecuteSqlRawAsync("UPDATE \"MiniAppSandboxSessions\" SET \"DroppedDiagnosticCount\"=100");await tx.RollbackAsync();
        }
        Assert.Equal(0,await f.Db.MiniAppSandboxSessions.Select(x=>x.DroppedDiagnosticCount).SingleAsync());
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous));await f.Db.Database.ExecuteSqlRawAsync(up);
        Assert.Equal(session.Id,await f.Db.MiniAppSandboxSessions.Select(x=>x.Id).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentObservationsCannotExceedThePersistedMinuteQuota()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        var service=new MiniAppSandboxDiagnosticService(f.Db,f.Service(),f.Clock);
        var batch=new SandboxDiagnosticBatch(Enumerable.Range(0,20).Select(_=>new SandboxDiagnosticEvent("Bridge","BRIDGE_READY")).ToArray());
        for(var i=0;i<3;i++)await service.IngestAsync(session.Id,SandboxEvidenceSource.Sdk,batch,default);
        var ready=0;var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Ingest()
        {
            await using var db=new AppDbContext(options);var sessions=new MiniAppSandboxSessionService(db,new MiniAppSandboxAccess(db,f.Clock,f.Options),f.Clock,f.Options);
            if(Interlocked.Increment(ref ready)==2)start.SetResult();await start.Task;
            try{await new MiniAppSandboxDiagnosticService(db,sessions,f.Clock).IngestAsync(session.Id,SandboxEvidenceSource.Sdk,new(batch.Events.Take(3).ToArray()),default);return true;}
            catch(DeveloperAuthException e) when(e.Status is 409 or 429){return false;}
        }
        Assert.Single(await Task.WhenAll(Ingest(),Ingest()),x=>x);Assert.Equal(63,await f.Db.MiniAppSandboxDiagnostics.CountAsync());
        var first=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,null,50,default);
        var second=await service.ListAsync(f.Actor,f.App.AppId,session.Id,null,first.NextCursor,50,default);
        Assert.Equal(63,first.Items.Concat(second.Items).Select(x=>x.Id).Distinct().Count());
    }

    [Fact]
    public async Task RetentionExpiryAndPurgingUseTheRealPostgresProvider()
    {
        await using var pg=await IsolatedPostgres.Start();await using var f=await SandboxFixture.Create(pg.ConnectionString);
        var session=await f.Service().CreateAsync(f.Actor,f.App.AppId,default);f.Db.Add(MiniAppSandboxEvidencePersistenceTests.Run(session.Id,session.ConfigurationFingerprint));await f.Db.SaveChangesAsync();
        f.Clock.Now+=TimeSpan.FromMinutes(20);var service=new MiniAppSandboxRetentionService(f.Db,f.Clock);
        var expired=await service.SweepAsync(100,default);Assert.Equal(1,expired.RevokedSessions);Assert.Equal(1,expired.EndedRuns);
        f.Clock.Now+=TimeSpan.FromDays(31);var purged=await service.SweepAsync(100,default);Assert.Equal(1,purged.PurgedRuns);Assert.Equal(1,purged.PurgedSessions);
        Assert.Single(await f.Db.MiniApps.AsNoTracking().ToListAsync());Assert.Single(await f.Db.DeveloperAccounts.AsNoTracking().ToListAsync());Assert.Single(await f.Db.MiniAppOAuthClients.AsNoTracking().ToListAsync());
    }
}
