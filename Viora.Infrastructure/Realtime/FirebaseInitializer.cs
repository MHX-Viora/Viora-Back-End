using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
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
            LogCredentialDiagnostics(firebaseOptions.ServiceAccountJson, "Firebase:ServiceAccountJson");
        }
        else if (!string.IsNullOrWhiteSpace(firebaseOptions.ServiceAccountJsonBase64))
        {
            var serviceAccountJson = DecodeBase64(firebaseOptions.ServiceAccountJsonBase64);
            credential = GoogleCredential.FromJson(serviceAccountJson);
            projectId = ReadProjectId(serviceAccountJson);
            LogCredentialDiagnostics(serviceAccountJson, "Firebase:ServiceAccountJsonBase64");
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
            var serviceAccountJson = File.ReadAllText(serviceAccountPath);
            projectId = ReadProjectId(serviceAccountJson);
            LogCredentialDiagnostics(serviceAccountJson, "Firebase:ServiceAccountPath");
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

    private static string DecodeBase64(string value)
    {
        var bytes = Convert.FromBase64String(value);
        return Encoding.UTF8.GetString(bytes);
    }

    private void LogCredentialDiagnostics(string serviceAccountJson, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(serviceAccountJson);
            var root = document.RootElement;
            var firebaseProjectId = root.TryGetProperty("project_id", out var projectIdElement)
                ? projectIdElement.GetString()
                : null;
            var clientEmail = root.TryGetProperty("client_email", out var emailElement)
                ? emailElement.GetString()
                : null;
            var hasPrivateKey = root.TryGetProperty("private_key", out var privateKeyElement) &&
                !string.IsNullOrWhiteSpace(privateKeyElement.GetString());

            if (string.IsNullOrWhiteSpace(firebaseProjectId))
            {
                logger.LogWarning("Could not parse Firebase service account project_id for diagnostics. Source: {Source}.", source);
                return;
            }

            logger.LogInformation(
                "Firebase service account diagnostics. Source: {Source}, ProjectId: {ProjectId}, ClientEmail: {ClientEmail}, HasPrivateKey: {HasPrivateKey}.",
                source,
                firebaseProjectId,
                clientEmail,
                hasPrivateKey);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not parse Firebase service account diagnostics. Source: {Source}.", source);
        }
    }
}
