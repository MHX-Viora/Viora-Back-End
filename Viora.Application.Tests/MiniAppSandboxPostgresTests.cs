using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Developers;
using Viora.Application.Developers;
using Xunit;

public sealed class MiniAppSandboxPostgresTests
{
    [Fact]
    public async Task ConcurrentCreatesAndLaunchRedemptionsHaveExactlyOneWinner()
    {
        await using var pg=await IsolatedPostgres.Start(); await using var fixture=await SandboxFixture.Create(pg.ConnectionString);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        async Task<SandboxSessionDto?> Create()
        {
            await using var db=new AppDbContext(options);
            var sessions=new MiniAppSandboxSessionService(db,new MiniAppSandboxAccess(db,fixture.Clock,fixture.Options),fixture.Clock,fixture.Options);
            try{return await sessions.CreateAsync(fixture.Actor,fixture.App.AppId,default);}
            catch(DeveloperAuthException error) when(error.Status==409){return null;}
            catch(PostgresException error) when(error.SqlState is "40001" or "40P01"){return null;}
            catch(DbUpdateException error) when(error.InnerException is PostgresException {SqlState:"40001" or "40P01"}){return null;}
        }
        var created=await Task.WhenAll(Create(),Create()); var session=Assert.Single(created,x=>x!=null)!;
        Assert.Equal(1,await fixture.Db.MiniAppSandboxSessions.CountAsync());
        var protection=new EphemeralDataProtectionProvider();
        var issuer=new MiniAppSandboxGrantService(fixture.Db,fixture.Service(),fixture.Clock,fixture.Options,protection);
        var browser=issuer.Prepare(); var launch=await issuer.IssueAsync(fixture.Actor,fixture.App.AppId,session.Id,browser.Nonce,default);
        async Task<bool> Redeem()
        {
            await using var db=new AppDbContext(options);
            var sessions=new MiniAppSandboxSessionService(db,new MiniAppSandboxAccess(db,fixture.Clock,fixture.Options),fixture.Clock,fixture.Options);
            var grants=new MiniAppSandboxGrantService(db,sessions,fixture.Clock,fixture.Options,protection);
            try{await grants.RedeemAsync(session.Id,launch.Grant,browser.Nonce,browser.Binding,default);return true;}
            catch(DeveloperAuthException){return false;}
            catch(PostgresException error) when(error.SqlState is "40001" or "40P01"){return false;}
            catch(DbUpdateException error) when(error.InnerException is PostgresException {SqlState:"40001" or "40P01"}){return false;}
        }
        Assert.Single(await Task.WhenAll(Redeem(),Redeem()),value=>value);
        Assert.NotNull((await fixture.Db.MiniAppSandboxLaunchGrants.AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.NotNull((await fixture.Db.MiniAppSandboxSessions.AsNoTracking().SingleAsync()).RuntimeCredentialHash);
    }

    [Fact]
    public async Task AdditiveMigrationConstraintsRollbackAndConcurrencyPreserveRegistry()
    {
        await using var pg=await IsolatedPostgres.Start();
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using var db=new AppDbContext(options); await db.Database.EnsureCreatedAsync();
        var owner=new DeveloperAccount {EmailNormalized="sandbox-pg@example.test",Status=DeveloperStatus.Active};
        var app=new MiniApp {Developer=owner,AppId="app_sandbox_pg",Name="PG Sandbox",Category="utilities"};
        var source=new DeveloperSession {Account=owner,TokenHash="source-pg",ExpiresAt=DateTime.UtcNow.AddHours(1),AbsoluteExpiresAt=DateTime.UtcNow.AddHours(2)};
        var client=new MiniAppOAuthClient {MiniApp=app,ClientKey="sandbox-pg-client",ClientType="Public",SandboxEnabled=true};
        db.DeveloperSessions.Add(source); db.MiniAppOAuthClients.Add(client); await db.SaveChangesAsync();
        var current=db.Database.GetMigrations().Last();
        var previous=db.Database.GetMigrations().Single(x=>x.EndsWith("_AddSsoAccountLinks"));
        var migrator=db.GetService<IMigrator>();
        // Recreate the pre-Sandbox schema before exercising all additive Sandbox migrations.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous));
        var up=migrator.GenerateScript(previous,current);
        Assert.DoesNotContain("DROP TABLE",up);Assert.DoesNotContain("DROP COLUMN",up);Assert.DoesNotContain("DELETE FROM",up);
        await db.Database.ExecuteSqlRawAsync(up);
        var session=new MiniAppSandboxSession {MiniAppId=app.Id,DeveloperAccountId=owner.Id,OAuthClientId=client.Id,
            ActorDeveloperAccountId=owner.Id,SourceDeveloperSessionId=source.Id,ExpiresAt=DateTime.UtcNow.AddMinutes(15),
            ConfigurationFingerprint=new string('a',64),WebsiteUrl="https://partner.example/",PartnerOrigin="https://partner.example",LastActivityAt=DateTime.UtcNow};
        db.MiniAppSandboxSessions.Add(session); await db.SaveChangesAsync();
        var grant=new MiniAppSandboxLaunchGrant {SandboxSessionId=session.Id,GrantHash=new string('b',64),BrowserNonceHash=new string('c',64),Audience="https://sandbox.example",ExpiresAt=DateTime.UtcNow.AddSeconds(30)};
        db.MiniAppSandboxLaunchGrants.Add(grant); await db.SaveChangesAsync();
        var denied=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppSandboxSessions\" SET \"Environment\"=1 WHERE \"Id\"={session.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation,denied.SqlState);
        denied=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MiniAppSandboxSessions\" SET \"ActorDeveloperAccountId\"=NULL WHERE \"Id\"={session.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation,denied.SqlState);
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {
            await db.MiniAppSandboxLaunchGrants.Where(x=>x.Id==grant.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,DateTime.UtcNow));
            await transaction.RollbackAsync();
        }
        Assert.Null(await db.MiniAppSandboxLaunchGrants.AsNoTracking().Where(x=>x.Id==grant.Id).Select(x=>x.ConsumedAt).SingleAsync());
        await using(var other=new AppDbContext(options))
        {
            var stale=await other.MiniAppSandboxSessions.SingleAsync(); session.Version=Guid.NewGuid(); await db.SaveChangesAsync();
            stale.Version=Guid.NewGuid(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>other.SaveChangesAsync());
        }
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous));
        Assert.Equal("app_sandbox_pg",await db.MiniApps.Select(x=>x.AppId).SingleAsync());
        await db.Database.ExecuteSqlRawAsync(up);
        Assert.Empty(await db.MiniAppSandboxSessions.AsNoTracking().ToListAsync());
        Assert.Single(await db.MiniAppOAuthClients.ToListAsync());
    }
}
