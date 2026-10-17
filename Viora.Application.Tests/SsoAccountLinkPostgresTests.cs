using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Application.Sso;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoAccountLinkPostgresTests
{
    [Fact]
    public async Task PostgreSqlMigrationConcurrentProofRedactionAndRollbackPreserveExistingIdentity()
    {
        await using var pg = await IsolatedPostgres.Start();
        await using var host = await SsoProtocolTestHost.Create(pg);
        const string secret = "isolated-postgres-link-test-secret";
        const string partnerId = "sensitive-local-partner-account-987";
        await host.MakeConfidential(secret);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"SsoAccountLinkAudits\"; DROP TABLE \"SsoAccountLinkChallenges\"; DROP TABLE \"MiniAppAccountLinks\"; CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
            var migrations = db.Database.GetMigrations().ToArray();
            var previous = migrations.Single(x => x.EndsWith("_AddMiniAppConsents"));
            var current = migrations.Single(x => x.EndsWith("_AddSsoAccountLinks"));
            var sql = db.GetService<IMigrator>().GenerateScript(previous, current);
            Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("ALTER TABLE", sql);
            await db.Database.ExecuteSqlRawAsync(sql); return true;
        });
        var grant = await host.WithServices(s => s.GetRequiredService<SsoConsentService>().GrantAsync(host.AccountId, SsoProtocolTestHost.ClientId, ["openid"], default));
        var proof = await host.WithServices(s => s.GetRequiredService<SsoAccountLinkService>().IssueAsync(SsoProtocolTestHost.ClientId, secret, grant.Subject, partnerId, default));
        async Task<SsoAccountLinkDto?> Confirm()
        {
            try { return await host.WithServices(s => s.GetRequiredService<SsoAccountLinkService>().ConfirmAsync(host.AccountId, proof.Challenge, default)); }
            catch (DeveloperAuthException) { return null; }
            catch (DbUpdateException) { return null; }
            catch (Npgsql.PostgresException error) when (error.SqlState is "40001" or "40P01") { return null; }
        }
        Assert.Single(await Task.WhenAll(Confirm(), Confirm()), x => x != null);
        await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var link = Assert.Single(await db.Set<MiniAppAccountLink>().ToListAsync());
            var challenge = Assert.Single(await db.Set<SsoAccountLinkChallenge>().ToListAsync());
            Assert.NotNull(challenge.ConsumedAt); Assert.Equal(64, challenge.ProofHash.Length);
            Assert.DoesNotContain(proof.Challenge, JsonSerializer.Serialize(challenge));
            var audit = JsonSerializer.Serialize(Assert.Single(await db.Set<SsoAccountLinkAudit>().ToListAsync()));
            foreach (var sensitive in new[] { secret, proof.Challenge, partnerId, grant.Subject, "@", "token", "verifier", "email" })
                Assert.DoesNotContain(sensitive, audit, StringComparison.OrdinalIgnoreCase);
            // Tombstones keep both identities reserved after unlink.
            Assert.True(await services.GetRequiredService<SsoAccountLinkService>().UnlinkAsync(host.AccountId, link.Id, default));
            db.Set<MiniAppAccountLink>().Add(new() { AccountId = link.AccountId, MiniAppId = link.MiniAppId,
                Environment = link.Environment, Subject = link.Subject, PartnerAccountId = "replacement" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var migrations = db.Database.GetMigrations().ToArray();
            var previous = migrations.Single(x => x.EndsWith("_AddMiniAppConsents"));
            var current = migrations.Single(x => x.EndsWith("_AddSsoAccountLinks"));
            var migrator = db.GetService<IMigrator>();
            await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current, previous));
            Assert.Equal(1, await db.Accounts.CountAsync());
            Assert.Equal(grant.Subject, (await db.MiniAppPairwiseSubjects.SingleAsync()).Subject);
            Assert.Equal(1, await db.MiniAppUserConsents.CountAsync());
            await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous, current));
            Assert.Empty(await db.Set<MiniAppAccountLink>().ToListAsync());
            return true;
        });
    }
}
