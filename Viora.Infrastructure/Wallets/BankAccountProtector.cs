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
