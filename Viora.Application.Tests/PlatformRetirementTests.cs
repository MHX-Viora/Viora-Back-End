using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text.RegularExpressions;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Migrations;
using Xunit;

public sealed class PlatformRetirementTests
{
    [Fact]
    public void DatabaseRemovalIsLimitedToRetiredTablesWithoutCascade()
    {
        var migration = new RemoveRetiredPartnerPlatformTables();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations)).Sql;
        Assert.StartsWith("DROP TABLE IF EXISTS", sql.Trim());
        Assert.EndsWith("RESTRICT;", sql.Trim());
        Assert.DoesNotContain("CASCADE", sql, StringComparison.OrdinalIgnoreCase);
        var tables = Regex.Matches(sql, "\\\"public\\\"\\.\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(25, tables.Length);
        Assert.Equal(tables.Length, tables.Distinct().Count());
        Assert.All(tables, name => Assert.True(name.StartsWith("MiniApp") ||
            name.StartsWith("Developer") || name.StartsWith("OpenIddict") || name == "__OpenApiMigrationsHistory"));
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
    }

    [Fact]
    public void RetirementMigrationPreservesAllExistingTablesAndRows()
    {
        var migration = new RetirePartnerPlatformPreserveData();
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
    }

    [Fact]
    public void ReintroducedRuntimeModelKeepsCoreFeaturesAndPlatformEntities()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=model_only;Username=fixture;Password=fixture").Options);
        var entities = db.Model.GetEntityTypes().Select(entity => entity.Name).ToArray();
        Assert.Contains("Viora.Domain.Entities.User", entities);
        Assert.Contains("Viora.Domain.Entities.Wallet", entities);
        Assert.Contains("Viora.Domain.Entities.Post", entities);
        Assert.Contains("Viora.Domain.Entities.MiniApp", entities);
        Assert.Contains("Viora.Domain.Entities.Developer", entities);
    }

    [Fact]
    public void ReintroducedApiHasPlatformControllersWithoutCustomSwaggerFilter()
    {
        var controllers = typeof(viora_BE.Controllers.AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);
        Assert.Contains(controllers, type => type.Name == "MiniAppsController");
        Assert.Contains(controllers, type => type.Name == "DeveloperProfileController");
        Assert.Null(typeof(viora_BE.Controllers.AuthController).Assembly.GetType("viora_BE.OpenApi.AuthorizeOperationFilter"));
    }
}
