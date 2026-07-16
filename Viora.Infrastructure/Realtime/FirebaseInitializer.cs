using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
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
            credential = GoogleCredential.FromFile(firebaseOptions.ServiceAccountPath);
            LogProjectId(File.ReadAllText(firebaseOptions.ServiceAccountPath));
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
