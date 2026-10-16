using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenIddict.EntityFrameworkCore.Models;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SsoConsentMigrationTests
{
    [Fact]
    public async Task PostgreSqlConsentMigrationPreservesPairwiseIdentityHistoryAndConcurrency()
    {
        await using var pg = await IsolatedPostgres.Start();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var previous = db.Database.GetMigrations().Single(x => x.EndsWith("_AddSsoLoginTransactions"));
        var current = db.Database.GetMigrations().Single(x => x.EndsWith("_AddMiniAppConsents"));
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"MiniAppUserConsents\"; DROP TABLE \"MiniAppPairwiseSubjects\"; CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
        var migrator = db.GetService<IMigrator>(); await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous,current));
        var owner = new DeveloperAccount { EmailNormalized = "consent-dev@example.test", TermsAcceptedAt = DateTime.UtcNow };
        var mini = new MiniApp { Developer = owner, AppId = "app_consent", Name = "Consent", Category = "utilities" };
        var account = new Account { Email = "consent@example.test" };
        var registryClient = new MiniAppOAuthClient { MiniApp = mini, ClientKey = "consent-client", ClientType = "Public" };
        var application = new OpenIddictEntityFrameworkCoreApplication { ClientId = "consent-client", ClientType = "public" };
        var authorization = new OpenIddictEntityFrameworkCoreAuthorization { Application = application, Subject = "pairwise-sandbox", Status = "valid", Type = "permanent" };
        db.Accounts.Add(account); db.MiniAppOAuthClients.Add(registryClient); db.Add(authorization);
        var subject = new MiniAppPairwiseSubject { AccountId = account.Id, MiniAppId = mini.Id, Subject = "pairwise-sandbox" };
        var consent = new MiniAppUserConsent { AccountId = account.Id, MiniAppId = mini.Id, OAuthClientId = registryClient.Id, GrantedScopes = "openid", AuthorizationId = authorization.Id, GrantedAt = DateTime.UtcNow };
        db.MiniAppPairwiseSubjects.Add(subject); db.MiniAppUserConsents.Add(consent); await db.SaveChangesAsync();
        db.MiniAppPairwiseSubjects.Add(new() { AccountId = account.Id, MiniAppId = mini.Id, Subject = "duplicate-sector" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.MiniAppPairwiseSubjects.Add(new() { AccountId = account.Id, MiniAppId = mini.Id, Environment = MiniAppOAuthEnvironment.Production, Subject = "pairwise-production" });
        await db.SaveChangesAsync();
        db.MiniAppUserConsents.Add(new() { AccountId = account.Id, MiniAppId = mini.Id, OAuthClientId = registryClient.Id, GrantedScopes = "openid" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await using (var other = new AppDbContext(options))
        {
            var first = await db.MiniAppUserConsents.SingleAsync(); var second = await other.MiniAppUserConsents.SingleAsync();
            first.Version = Guid.NewGuid(); first.GrantedScopes = "openid profile"; await db.SaveChangesAsync();
            second.Version = Guid.NewGuid(); second.GrantedScopes = "openid email";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        }
        await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().ExecuteDeleteAsync();
        db.ChangeTracker.Clear(); Assert.Null((await db.MiniAppUserConsents.SingleAsync()).AuthorizationId);
        Assert.Equal("pairwise-sandbox", (await db.MiniAppPairwiseSubjects.SingleAsync(x => x.Environment == MiniAppOAuthEnvironment.Sandbox)).Subject);
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(current,previous)); Assert.Equal(1, await db.Accounts.CountAsync());
        await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(previous,current));
    }
}
