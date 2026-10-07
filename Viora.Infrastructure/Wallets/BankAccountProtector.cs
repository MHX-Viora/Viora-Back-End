using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Viora.Application.Wallets;

namespace Viora.Infrastructure.Wallets;

public sealed class BankAccountProtector(IOptions<WithdrawalOptions> options) : IBankAccountProtector
{
    public string Protect(string accountNumber)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plain = Encoding.UTF8.GetBytes(accountNumber);
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Hash(string accountNumber)
    {
        using var hmac = new HMACSHA256(GetKey());
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(accountNumber))).ToLowerInvariant();
    }

    public string Unprotect(string encrypted)
    {
        var bytes = Convert.FromBase64String(encrypted);
        if (bytes.Length < 29) throw new CryptographicException("Invalid encrypted bank account.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(GetKey(), 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetKey()
    {
        try
        {
            var key = Convert.FromBase64String(options.Value.BankAccountEncryptionKey);
            if (key.Length == 32) return key;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("Wallet:BankAccountEncryptionKey must be a base64-encoded 32-byte key.");
    }
}
