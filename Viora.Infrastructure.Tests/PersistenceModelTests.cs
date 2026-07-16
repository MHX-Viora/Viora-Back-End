using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viora.Domain.Entities;
using Viora.Infrastructure;
using Viora.Infrastructure.Persistence;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class PersistenceModelTests
{
    private static readonly AppDbContext Context = new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=viora_model_tests;Username=test")
            .Options);

    [Fact]
    public void Model_maps_all_documented_tables()
    {
        var tables = Context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(26, tables.Count);
        Assert.Contains("Accounts", tables);
        Assert.Contains("RefreshTokens", tables);
        Assert.Contains("Users", tables);
        Assert.Contains("Posts", tables);
        Assert.Contains("Conversations", tables);
        Assert.Contains("DeviceTokens", tables);
        Assert.Contains("Reports", tables);
    }

    [Fact]
    public void Identity_allows_only_one_pending_submission_per_user()
    {
        var entity = Context.Model.FindEntityType(typeof(UserIdentity));
        var index = Assert.Single(entity!.GetIndexes(), value =>
            value.IsUnique && value.Properties.Select(property => property.Name).SequenceEqual([nameof(UserIdentity.UserId)]));

        Assert.Equal("\"Status\" = 1", index.GetFilter());
    }

    [Fact]
    public void Relationships_are_restrictive_by_default()
    {
        var unexpected = Context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.ClientSetNull)
            .ToList();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void Time_columns_use_postgresql_utc_type()
    {
        var createdAt = Context.Model.FindEntityType(typeof(Account))!
            .FindProperty(nameof(Account.CreatedAt));

        Assert.Equal("timestamp with time zone", createdAt!.GetColumnType());
    }

    [Fact]
    public void AddInfrastructure_rejects_missing_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_rejects_missing_jwt_signing_key()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=viora;Username=test"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(configuration));

        Assert.Contains("Jwt:Key", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_rejects_missing_cloudinary_configuration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=viora;Username=test",
                ["Jwt:Key"] = "test-signing-key-with-at-least-32-bytes"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(configuration));

        Assert.Contains("Cloudinary", exception.Message);
    }

    [Fact]
    public void Initial_migration_prevents_reversed_friendship_duplicates()
    {
        var migrations = Context.GetService<IMigrationsAssembly>();
        var migrationType = Assert.Single(
            migrations.Migrations,
            migration => migration.Key.EndsWith("_InitialCreate", StringComparison.Ordinal)).Value;
        var migration = migrations.CreateMigration(migrationType, Context.Database.ProviderName!);

        var operation = Assert.Single(migration.UpOperations.OfType<SqlOperation>(),
            sql => sql.Sql.Contains("UX_Friendships_UnorderedPair", StringComparison.Ordinal));

        Assert.Contains("LEAST", operation.Sql);
        Assert.Contains("GREATEST", operation.Sql);
    }

    [Fact]
    public void Design_factory_rejects_missing_appsettings_file()
    {
        WithTemporaryWorkingDirectory(null, () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppDbContextFactory().CreateDbContext([]));

            Assert.Contains("appsettings.json", exception.Message);
        });
    }

    [Fact]
    public void Design_factory_uses_appsettings_connection_string()
    {
        const string json = """
            {
              "ConnectionStrings": {
                "DefaultConnection": "Host=db.example;Database=viora;Username=test;Password=test;SSL Mode=Require"
              }
            }
            """;

        WithTemporaryWorkingDirectory(json, () =>
        {
            using var context = new AppDbContextFactory().CreateDbContext([]);
            var connectionString = context.Database.GetDbConnection().ConnectionString;

            Assert.Contains("Host=db.example", connectionString);
            Assert.DoesNotContain("Host=localhost", connectionString);
        });
    }

    [Fact]
    public void Design_factory_rejects_postgresql_uri_format()
    {
        const string json = """
            {
              "ConnectionStrings": {
                "DefaultConnection": "postgresql://user:password@db.example/viora?sslmode=require"
              }
            }
            """;

        WithTemporaryWorkingDirectory(json, () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppDbContextFactory().CreateDbContext([]));

            Assert.Contains("Host=...;Database=...", exception.Message);
            Assert.DoesNotContain("user:password", exception.Message);
        });
    }

    private static void WithTemporaryWorkingDirectory(string? appSettingsJson, Action action)
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var temporaryDirectory = Directory.CreateTempSubdirectory("viora-factory-tests-");

        try
        {
            if (appSettingsJson is not null)
            {
                var apiDirectory = Directory.CreateDirectory(
                    Path.Combine(temporaryDirectory.FullName, "viora-BE"));
                File.WriteAllText(Path.Combine(apiDirectory.FullName, "appsettings.json"), appSettingsJson);
            }

            Directory.SetCurrentDirectory(temporaryDirectory.FullName);
            action();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            temporaryDirectory.Delete(recursive: true);
        }
    }
}
