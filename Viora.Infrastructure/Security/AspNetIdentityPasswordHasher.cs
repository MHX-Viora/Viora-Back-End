using Microsoft.AspNetCore.Identity;
using Viora.Application.Accounts;

namespace Viora.Infrastructure.Security;

public sealed class AspNetIdentityPasswordHasher : IPasswordHasher
{
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

        return hasher.VerifyHashedPassword(User, passwordHash, password) != PasswordVerificationResult.Failed;
    }
}
