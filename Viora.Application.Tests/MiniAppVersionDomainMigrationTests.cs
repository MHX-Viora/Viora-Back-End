using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Viora.Domain.Developers;
using Xunit;

public sealed class MiniAppVersionDomainMigrationTests
{
    [Fact]
    public async Task HostnameIndexRoundTripsAndDowngradeRefusesMultipleLiveHosts()
    {
        await using var pg = await IsolatedPostgres.Start(); await using var f = await SandboxFixture.Create(pg.ConnectionString);
        var (previous, current, migrator) = await MiniAppReviewMigrationTests.Prepare(f.Db, "ScopeMiniAppDomainProofsByHostname");
        var extra = MiniAppVersionDomainTests.Proof(f.App.Id, "other.example.com", "domain-migration-other.txt");
        f.Db.Add(extra); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Db.Add(MiniAppVersionDomainTests.Proof(f.App.Id, f.Proof.Hostname, "domain-migration-duplicate.txt"));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync()); f.Db.ChangeTracker.Clear();
        var down = migrator.GenerateScript(current, previous, MigrationsSqlGenerationOptions.NoTransactions);
        await MiniAppReviewMigrationTests.Denied(f.Db, down);
        // Explicit operator resolution in the disposable fixture; migration never revokes proof automatically.
        await f.Db.MiniAppDomainVerifications.Where(x => x.Id == extra.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.RevokedAt, DateTime.UtcNow).SetProperty(x => x.Status, MiniAppDomainStatus.Revoked));
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
        Assert.Equal(2, await f.Db.MiniAppDomainVerifications.CountAsync());
        Assert.Equal(MiniAppDomainStatus.Verified, await f.Db.MiniAppDomainVerifications.Where(x => x.Id == f.Proof.Id).Select(x => x.Status).SingleAsync());
        Assert.Single(await f.Db.MiniAppOAuthClients.ToListAsync()); Assert.Single(await f.Db.DeveloperSessions.ToListAsync());
        await f.Db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
        f.Db.Add(MiniAppVersionDomainTests.Proof(f.App.Id, "another.example.com", "domain-migration-final.txt"));
        await f.Db.SaveChangesAsync(); Assert.Equal(2, await f.Db.MiniAppDomainVerifications.CountAsync(x => x.RevokedAt == null));
    }
}
