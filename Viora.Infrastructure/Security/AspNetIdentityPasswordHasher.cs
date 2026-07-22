using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using Viora.Application.Accounts;

namespace Viora.Infrastructure.Security;

public sealed class AspNetIdentityPasswordHasher : IPasswordHasher
{
    private const string LegacyAlgorithmName = "pbkdf2-sha256";
    private readonly PasswordHasher<object> hasher = new();
    private static readonly object User = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return hasher.HashPassword(User, password);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            return hasher.VerifyHashedPassword(User, passwordHash, password) != PasswordVerificationResult.Failed ||
                VerifyLegacyPbkdf2(password, passwordHash);
        }
        catch (FormatException)
        {
            return VerifyLegacyPbkdf2(password, passwordHash);
        }
    }

    private static bool VerifyLegacyPbkdf2(string password, string passwordHash)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != LegacyAlgorithmName ||
            !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
