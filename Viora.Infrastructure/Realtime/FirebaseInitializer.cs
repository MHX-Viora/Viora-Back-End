using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Viora.Infrastructure.Realtime;

public interface IFirebaseInitializer
{
    string? ProjectId { get; }
    FirebaseApp? GetApp();
}

public sealed class FirebaseInitializer(
    IOptions<FirebaseOptions> options,
    IHostEnvironment environment,
    ILogger<FirebaseInitializer> logger) : IFirebaseInitializer
{
    private readonly object syncRoot = new();
    private FirebaseApp? app;
    private bool initialized;
    private string? projectId;

    public string? ProjectId => projectId;

    public FirebaseApp? GetApp()
    {
        if (initialized)
        {
            return app;
        }

        lock (syncRoot)
        {
            if (initialized)
            {
                return app;
            }

            app = FirebaseApp.DefaultInstance ?? CreateApp();
            initialized = true;
            return app;
        }
    }

    private FirebaseApp? CreateApp()
    {
        var firebaseOptions = options.Value;
        GoogleCredential? credential = null;

        if (!string.IsNullOrWhiteSpace(firebaseOptions.ServiceAccountJson))
        {
            credential = GoogleCredential.FromJson(firebaseOptions.ServiceAccountJson);
            projectId = ReadProjectId(firebaseOptions.ServiceAccountJson);
            LogProjectId(projectId);
        }
        else if (!string.IsNullOrWhiteSpace(firebaseOptions.ServiceAccountPath))
        {
            var serviceAccountPath = ResolveServiceAccountPath(firebaseOptions.ServiceAccountPath);
            if (serviceAccountPath is null)
            {
                logger.LogError(
                    "Firebase service account file not found. ConfiguredPath: {ConfiguredPath}, ContentRootPath: {ContentRootPath}, AppBaseDirectory: {AppBaseDirectory}.",
                    firebaseOptions.ServiceAccountPath,
                    environment.ContentRootPath,
                    AppContext.BaseDirectory);
                return null;
            }

            credential = GoogleCredential.FromFile(serviceAccountPath);
            logger.LogInformation("Firebase service account loaded from {ServiceAccountPath}.", serviceAccountPath);
            projectId = ReadProjectId(File.ReadAllText(serviceAccountPath));
            LogProjectId(projectId);
        }

        if (credential is null)
        {
            logger.LogWarning(
                "Firebase push disabled. Configure Firebase:ServiceAccountJson or Firebase:ServiceAccountPath.");
            return null;
        }

        var firebaseApp = FirebaseApp.Create(new AppOptions
        {
            Credential = credential
        });
        logger.LogInformation("Firebase Admin app initialized.");
        return firebaseApp;
    }

    private string? ResolveServiceAccountPath(string configuredPath)
    {
        var candidates = Path.IsPathRooted(configuredPath)
            ? [configuredPath]
            :
            new[]
            {
                Path.Combine(environment.ContentRootPath, configuredPath),
                Path.Combine(AppContext.BaseDirectory, configuredPath),
                Path.Combine(Directory.GetCurrentDirectory(), configuredPath)
            };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ReadProjectId(string serviceAccountJson)
    {
        try
        {
            using var document = JsonDocument.Parse(serviceAccountJson);
            return document.RootElement.TryGetProperty("project_id", out var projectId)
                ? projectId.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void LogProjectId(string? firebaseProjectId)
    {
        if (string.IsNullOrWhiteSpace(firebaseProjectId))
        {
            logger.LogWarning("Could not parse Firebase service account project_id for diagnostics.");
            return;
        }

        logger.LogInformation(
            "Firebase service account project_id: {ProjectId}. Confirm it matches android google-services.json.",
            firebaseProjectId);
    }
}
