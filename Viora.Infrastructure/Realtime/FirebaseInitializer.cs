using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        }
        else if (!string.IsNullOrWhiteSpace(firebaseOptions.ServiceAccountPath))
        {
            credential = GoogleCredential.FromFile(firebaseOptions.ServiceAccountPath);
        }

        if (credential is null)
        {
            logger.LogWarning(
                "Firebase push disabled. Configure Firebase:ServiceAccountJson or Firebase:ServiceAccountPath.");
            return null;
        }

        return FirebaseApp.Create(new AppOptions
        {
            Credential = credential
        });
    }
}
