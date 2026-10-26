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
        var identity = new SandboxTestIdentity { Developer = owner, MiniApp = app, Account = legacy };
        var session = new SandboxIdentitySession { Identity = identity, DeveloperSession = source, OAuthClient = client,
            ClientVersion = client.Version, LaunchHash = new string('a', 64), LaunchExpiresAt = DateTime.UtcNow.AddMinutes(1), ExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        db.Accounts.Add(real);
        db.SandboxIdentityTemplates.Add(new() { Id = SandboxIdentityTemplate.DefaultId, Account = shared, FullName = "ANKT Sandbox User", Nickname = "ankt_sandbox" });
        db.SandboxIdentitySessions.Add(session); await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(shared.Id, await db.SandboxTestIdentities.Select(x => x.AccountId).SingleAsync());
        Assert.NotNull((await db.SandboxIdentitySessions.SingleAsync()).RevokedAt);
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
