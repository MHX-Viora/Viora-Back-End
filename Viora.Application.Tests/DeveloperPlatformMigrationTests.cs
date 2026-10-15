using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Viora.Infrastructure.Persistence.Migrations;
using Xunit;

public sealed class DeveloperPlatformMigrationTests
{
    [Fact]
    public void UpgradePreservesExistingDataAndRequiresNoFabricatedVerificationBackfill()
    {
        var migration = new UpgradeDeveloperPlatform { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        Assert.DoesNotContain(migration.UpOperations, op => op is DropTableOperation or DropColumnOperation);
        foreach (var column in migration.UpOperations.OfType<AddColumnOperation>()) Assert.True(column.IsNullable);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), index => index.Table == "DeveloperMemberships" && index.Columns.SequenceEqual(["AccountId"]) && !index.IsUnique);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), index => index.Table == "DeveloperReauths" && index.Columns.SequenceEqual(["ProviderAuthenticationHash"]) && index.IsUnique);
        Assert.DoesNotContain(migration.UpOperations.OfType<SqlOperation>(), operation => operation.Sql.Contains("UPDATE \"Accounts\""));
    }
    [Fact]
    public void RollbackRefusesToDiscardNewRolesChallengesSecretsAndVerifiedIdentities()
    {
        var migration = new UpgradeDeveloperPlatform { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("RAISE EXCEPTION", guard.Sql);
        foreach (var resource in new[] { "MiniAppMemberships", "MiniAppMemberInvitations", "VerificationFileName", "PreviousClientSecretHash", "EmailVerifiedAt", "ProviderAuthenticationHash" })
            Assert.Contains(resource, guard.Sql);
    }
}
