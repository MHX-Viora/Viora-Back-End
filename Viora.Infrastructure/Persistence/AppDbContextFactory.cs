using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using System.Text.Json;

namespace Viora.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var appSettingsPath = FindAppSettingsPath();
        var connectionString = ReadConnectionString(appSettingsPath);

        try
        {
            connectionString = new NpgsqlConnectionStringBuilder(connectionString).ConnectionString;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection in appsettings.json is not a valid Npgsql connection string. " +
                "The postgresql:// URI format is not supported here; use Host=...;Database=... format.",
                exception);
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    private static string FindAppSettingsPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(currentDirectory, "appsettings.json"),
            Path.Combine(currentDirectory, "viora-BE", "appsettings.json"),
            Path.GetFullPath(Path.Combine(currentDirectory, "..", "viora-BE", "appsettings.json"))
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException(
                "Could not find viora-BE/appsettings.json. Run dotnet ef from the solution root, " +
                "the viora-BE project, or the Viora.Infrastructure project.");
    }

    private static string ReadConnectionString(string appSettingsPath)
    {
        try
        {
            using var stream = File.OpenRead(appSettingsPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
                connectionStrings.TryGetProperty("DefaultConnection", out var defaultConnection))
            {
                var value = defaultConnection.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("appsettings.json contains invalid JSON.", exception);
        }

        throw new InvalidOperationException(
            "Missing required configuration 'ConnectionStrings:DefaultConnection' in appsettings.json.");
    }
}
