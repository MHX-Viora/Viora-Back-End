using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;
using Viora.Application.Accounts;
using Viora.Infrastructure.Realtime;

namespace Viora.Infrastructure.Security;

public sealed class FirebasePhoneTokenVerifier(
    IFirebaseInitializer firebaseInitializer,
    ILogger<FirebasePhoneTokenVerifier> logger) : IFirebasePhoneTokenVerifier
{
    public async Task<string?> VerifyPhoneNumberAsync(
        string firebaseToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(firebaseToken))
        {
            return null;
        }

        var app = firebaseInitializer.GetApp();
        if (app is null)
        {
            logger.LogError("Firebase Admin is not configured for password reset.");
            return null;
        }

        try
        {
            var decodedToken = await FirebaseAuth.GetAuth(app)
                .VerifyIdTokenAsync(firebaseToken, cancellationToken);
            return decodedToken.Claims.TryGetValue("phone_number", out var phoneNumber)
                ? phoneNumber?.ToString()
                : null;
        }
        catch (FirebaseAuthException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase ID token verification failed during password reset.");
            return null;
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase ID token was empty or malformed during password reset.");
            return null;
        }
    }
}

public sealed class BCryptPasswordResetHasher : IPasswordResetHasher
{
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }
}

