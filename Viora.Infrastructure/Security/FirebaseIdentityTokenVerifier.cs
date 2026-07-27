using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;
using Viora.Application.Accounts;
using Viora.Infrastructure.Realtime;

namespace Viora.Infrastructure.Security;

public sealed class FirebaseIdentityTokenVerifier(
    IFirebaseInitializer firebaseInitializer,
    ILogger<FirebaseIdentityTokenVerifier> logger) : IFirebaseIdentityTokenVerifier
{
    public async Task<FirebaseVerifiedIdentity?> VerifyAsync(
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
            var phoneNumber = decodedToken.Claims.TryGetValue("phone_number", out var phoneClaim)
                ? phoneClaim?.ToString()
                : null;
            var emailVerified =
                decodedToken.Claims.TryGetValue("email_verified", out var verifiedClaim) &&
                bool.TryParse(verifiedClaim?.ToString(), out var isVerified) &&
                isVerified;
            var email =
                emailVerified && decodedToken.Claims.TryGetValue("email", out var emailClaim)
                    ? emailClaim?.ToString()?.Trim().ToLowerInvariant()
                    : null;

            return string.IsNullOrWhiteSpace(phoneNumber) && string.IsNullOrWhiteSpace(email)
                ? null
                : new FirebaseVerifiedIdentity(email, phoneNumber);
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
