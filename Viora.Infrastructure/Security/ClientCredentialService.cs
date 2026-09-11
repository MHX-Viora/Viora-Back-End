using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Viora.Application.MiniApps;

namespace Viora.Infrastructure.Security;

public sealed class ClientCredentialService : IClientCredentialService
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> hasher = new();

    public string CreateClientId() => "ank_" + RandomToken(18);
    public string CreateSecret() => "sec_" + RandomToken(32);
    public string CreateLaunchCode() => "LCH_" + RandomToken(32);
    public string CreateSubject() => "sub_" + RandomToken(24);
    public string HashSecret(string secret) => hasher.HashPassword(Subject, secret);

    public bool VerifySecret(string secret, string hash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(hash)) return false;
        try { return hasher.VerifyHashedPassword(Subject, hash, secret) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }

    public string HashLaunchCode(string code) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    private static string RandomToken(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
