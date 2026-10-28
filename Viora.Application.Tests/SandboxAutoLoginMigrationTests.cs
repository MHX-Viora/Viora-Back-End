using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class SandboxAutoLoginMigrationTests
{
    [Fact]
    public async Task UpgradeRebindsLegacyIdentityRevokesSessionsAndPreservesProductionAccount()
    {
        await using var postgres = await IsolatedPostgres.Start();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var target = db.Database.GetMigrations().Single(x => x.EndsWith("_ShareSandboxTestAccount"));
        var previous = db.Database.GetMigrations().Last(x => string.CompareOrdinal(x, target) < 0);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var shared = new Account { IsSandbox = true, Email = "sandbox@example.test", User = new User { DisplayName = "ANKT Test" } };
        var legacy = new Account { IsSandbox = true, Email = "sandbox+old@example.test", User = new User { DisplayName = "ANKT Test" } };
        var real = new Account { Email = "real@example.test", User = new User { DisplayName = "ANKT Test" } };
        var owner = new DeveloperAccount { FullName = "Upgrade owner", EmailNormalized = "UPGRADE@EXAMPLE.TEST", Status = DeveloperStatus.Active };
        var app = new MiniApp { Developer = owner, AppId = "app_" + new string('a', 43), Name = "Upgrade", Category = "utilities" };
        var client = new MiniAppOAuthClient { MiniApp = app, ClientKey = "upgrade-sandbox", ClientType = "Public", SandboxEnabled = true };
        var source = new DeveloperSession { Account = owner, TokenHash = "upgrade-source", ExpiresAt = DateTime.UtcNow.AddHours(1), AbsoluteExpiresAt = DateTime.UtcNow.AddHours(1) };
        var identityId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var now = DateTime.UtcNow;
        db.Accounts.Add(real);
        db.SandboxIdentityTemplates.Add(new() { Id = SandboxIdentityTemplate.DefaultId, Account = shared, FullName = "ANKT Sandbox User", Nickname = "ankt_sandbox" });
        db.AddRange(legacy, client, source); await db.SaveChangesAsync();
        // Seed the historical schema directly: retired cookie-login types must
        // not be restored to the current runtime model just to test an upgrade.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SandboxTestIdentities" ("Id", "DeveloperAccountId", "MiniAppId", "AccountId", "CreatedAt")
            VALUES ({identityId}, {owner.Id}, {app.Id}, {legacy.Id}, {now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SandboxIdentitySessions" ("Id", "IdentityId", "DeveloperSessionId", "OAuthClientId", "ClientVersion",
                "LaunchHash", "LaunchExpiresAt", "ExpiresAt", "CreatedAt")
            VALUES ({sessionId}, {identityId}, {source.Id}, {client.Id}, {client.Version},
                {new string('a', 64)}, {now.AddMinutes(1)}, {now.AddMinutes(15)}, {now})
            """);
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(shared.Id, await db.Database.SqlQuery<Guid>($"""
            SELECT "AccountId" AS "Value" FROM legacy_sso."SandboxTestIdentities" WHERE "Id"={identityId}
            """).SingleAsync());
        Assert.NotNull(await db.Database.SqlQuery<DateTime?>($"""
            SELECT "RevokedAt" AS "Value" FROM legacy_sso."SandboxIdentitySessions" WHERE "Id"={sessionId}
            """).SingleAsync());
        var profile = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.AccountId == shared.Id);
        Assert.Equal("ANKT Sandbox User", profile.DisplayName); Assert.True(profile.IsVerified);
        Assert.Equal("sandbox-test@domain.test", (await db.Accounts.IgnoreQueryFilters().SingleAsync(x => x.Id == shared.Id)).Email);
        Assert.Equal("ANKT Developer Test", (await db.SandboxIdentityTemplates.SingleAsync()).FullName);
        Assert.Equal("ANKT Test", (await db.Users.SingleAsync(x => x.AccountId == real.Id)).DisplayName);
        Assert.True(await db.Accounts.IgnoreQueryFilters().AnyAsync(x => x.Id == legacy.Id));
        // Applying migrations twice must not duplicate or replace either user.
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Accounts.IgnoreQueryFilters().CountAsync());
    }
}
