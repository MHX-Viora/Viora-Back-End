using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viora.Application.Accounts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "viora-BE";
    public string Audience { get; set; } = "viora-client";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class JwtTokenService : ITokenService
{
    private static readonly byte[] Header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" });
    private readonly JwtOptions options;
    private readonly byte[] signingKey;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        this.options = options.Value;
        signingKey = Encoding.UTF8.GetBytes(this.options.Key);
        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes.");
        }
        if (this.options.AccessTokenMinutes <= 0 || this.options.RefreshTokenDays <= 0)
        {
            throw new InvalidOperationException("JWT token lifetimes must be greater than zero.");
        }
    }

    public IssuedAccountTokens CreateTokens(Account account)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshToken = Base64Url(RandomNumberGenerator.GetBytes(64));
        var refreshExpiresAt = now.AddDays(options.RefreshTokenDays);
        return new IssuedAccountTokens(
            new AccountTokens(
                CreateToken(account, "access", now, now.AddMinutes(options.AccessTokenMinutes)),
                refreshToken),
            HashRefreshToken(refreshToken),
            refreshExpiresAt.UtcDateTime);
    }

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private string CreateToken(
        Account account,
        string tokenType,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = options.Issuer,
            ["aud"] = options.Audience,
            ["sub"] = account.Id.ToString(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["role"] = (short)account.Role,
            ["token_type"] = tokenType
        };

        if (account.User is not null)
        {
            payload["user_id"] = account.User.Id.ToString();
        }

        var unsignedToken = $"{Base64Url(Header)}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        using var hmac = new HMACSHA256(signingKey);
        var signature = hmac.ComputeHash(Encoding.ASCII.GetBytes(unsignedToken));
        return $"{unsignedToken}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
