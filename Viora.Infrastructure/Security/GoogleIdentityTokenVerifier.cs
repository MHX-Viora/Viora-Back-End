using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;
using Viora.Application.Accounts;
using Viora.Infrastructure.Realtime;

namespace Viora.Infrastructure.Security;

public sealed class GoogleIdentityTokenVerifier(
    IFirebaseInitializer firebaseInitializer,
    ILogger<GoogleIdentityTokenVerifier> logger) : IGoogleIdentityTokenVerifier
{
    private const string GoogleProvider = "google.com";

    public async Task<GoogleVerifiedIdentity?> VerifyAsync(
        string firebaseToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(firebaseToken)) return null;

        var app = firebaseInitializer.GetApp();
        if (app is null)
        {
            logger.LogError("Firebase Admin is not configured for Google login.");
            return null;
        }

        try
        {
            var decoded = await FirebaseAuth.GetAuth(app)
                .VerifyIdTokenAsync(firebaseToken, checkRevoked: true, cancellationToken);
            var emailVerified = TryGetBoolean(decoded.Claims, "email_verified");
            var email = TryGetString(decoded.Claims, "email")?.Trim().ToLowerInvariant();
            var provider = TryGetNestedString(
                decoded.Claims,
                "firebase",
                "sign_in_provider");

            return emailVerified && provider == GoogleProvider &&
                !string.IsNullOrWhiteSpace(decoded.Uid) &&
                !string.IsNullOrWhiteSpace(email)
                ? new GoogleVerifiedIdentity(decoded.Uid, email)
                : null;
        }
        catch (FirebaseAuthException exception)
        {
            logger.LogWarning(exception, "Firebase token verification failed for Google login.");
            return null;
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Firebase token was malformed for Google login.");
            return null;
        }
    }

    private static string? TryGetString(
        IReadOnlyDictionary<string, object> claims,
        string key) => claims.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool TryGetBoolean(
        IReadOnlyDictionary<string, object> claims,
        string key) => claims.TryGetValue(key, out var value) &&
        bool.TryParse(value?.ToString(), out var result) && result;

    private static string? TryGetNestedString(
        IReadOnlyDictionary<string, object> claims,
        string parentKey,
        string childKey)
    {
        if (!claims.TryGetValue(parentKey, out var parent)) return null;
        if (parent is IReadOnlyDictionary<string, object> readOnly &&
            readOnly.TryGetValue(childKey, out var readOnlyValue))
        {
            return readOnlyValue?.ToString();
        }

        if (parent is IDictionary<string, object> dictionary &&
            dictionary.TryGetValue(childKey, out var value))
        {
            return value?.ToString();
        }

        return null;
    }
}
