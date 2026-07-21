using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Viora.Infrastructure.Realtime;

public interface IFirebaseInitializer
{
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
            LogProjectId(firebaseOptions.ServiceAccountJson);
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
            LogProjectId(File.ReadAllText(serviceAccountPath));
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

    private void LogProjectId(string serviceAccountJson)
    {
        try
        {
            using var document = JsonDocument.Parse(serviceAccountJson);
            if (document.RootElement.TryGetProperty("project_id", out var projectId))
            {
                logger.LogInformation(
                    "Firebase service account project_id: {ProjectId}. Confirm it matches android google-services.json.",
                    projectId.GetString());
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not parse Firebase service account project_id for diagnostics.");
        }
    }
}
