using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppOptions
{
    public int MaxAppsPerDeveloper { get; set; } = 50;
    public string ActiveKeyVersion { get; set; } = "";
    public Dictionary<string, string> EncryptionKeys { get; set; } = new();
}

// Versioned external keys allow old ciphertext to remain readable during key rotation.
public sealed class MiniAppSecretProtector(IOptions<MiniAppOptions> options)
{
    public (string Ciphertext, string Version) Encrypt(string secret, Guid clientId, string purpose = "mini-app")
    {
        var version = options.Value.ActiveKeyVersion;
        var key = Key(version);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var plaintext = Encoding.UTF8.GetBytes(secret);
            var ciphertext = new byte[plaintext.Length]; var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes($"{purpose}:{clientId:D}:{version}"));
            CryptographicOperations.ZeroMemory(plaintext);
            return (Convert.ToBase64String(nonce.Concat(tag).Concat(ciphertext).ToArray()), version);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public string Decrypt(string encrypted, string version, Guid clientId, string purpose = "mini-app")
    {
        var key = Key(version);
        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            if (bytes.Length < 29) throw new CryptographicException();
            var plaintext = new byte[bytes.Length - 28];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext,
                    Encoding.UTF8.GetBytes($"{purpose}:{clientId:D}:{version}"));
                return Encoding.UTF8.GetString(plaintext);
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        { throw Unavailable(); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private byte[] Key(string version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 40 || !options.Value.EncryptionKeys.TryGetValue(version, out var value)) throw Unavailable();
        try { var key = Convert.FromBase64String(value); if (key.Length == 32) return key; }
        catch (FormatException) { }
        throw Unavailable();
    }
    private static DeveloperAuthException Unavailable() => new(503, "CREDENTIALS_UNAVAILABLE", "Khóa bảo vệ thông tin tích hợp chưa sẵn sàng.");
}
