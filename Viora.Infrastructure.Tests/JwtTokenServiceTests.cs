using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Viora.Infrastructure.Security;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateTokens_returns_access_and_opaque_refresh_tokens_without_personal_identifiers()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Key = "test-signing-key-with-at-least-32-bytes",
            Issuer = "viora-tests",
            Audience = "viora-client"
        }));
        var account = new Account
        {
            Email = "private@example.com",
            Phone = "0901234567",
            PasswordHash = "secret-hash",
            Role = AccountRole.User,
            User = new User { DisplayName = "Test User" }
        };

        var issue = service.CreateTokens(account);
        var tokens = issue.Tokens;
        var accessPayload = DecodePayload(tokens.AccessToken);

        Assert.Equal("access", accessPayload.GetProperty("token_type").GetString());
        Assert.DoesNotContain("private@example.com", tokens.AccessToken);
        Assert.DoesNotContain("secret-hash", tokens.AccessToken);
        Assert.Equal(3, tokens.AccessToken.Split('.').Length);
        Assert.Equal(2, typeof(AccountTokens).GetProperties().Length);
        Assert.NotEmpty(tokens.RefreshToken);
        Assert.NotEqual(tokens.RefreshToken, issue.RefreshTokenHash);
        Assert.Equal(issue.RefreshTokenHash, service.HashRefreshToken(tokens.RefreshToken));
        Assert.True(issue.RefreshTokenExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public void Constructor_rejects_short_signing_key()
    {
        var options = Options.Create(new JwtOptions { Key = "too-short" });

        var exception = Assert.Throws<InvalidOperationException>(() => new JwtTokenService(options));

        Assert.Contains("32", exception.Message);
    }

    [Fact]
    public void Constructor_rejects_non_positive_token_lifetime()
    {
        var options = Options.Create(new JwtOptions
        {
            Key = "test-signing-key-with-at-least-32-bytes",
            RefreshTokenDays = 0
        });

        var exception = Assert.Throws<InvalidOperationException>(() => new JwtTokenService(options));

        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public async Task Access_token_is_accepted_by_standard_jwt_validation()
    {
        const string key = "test-signing-key-with-at-least-32-bytes";
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Key = key,
            Issuer = "viora-tests",
            Audience = "viora-client"
        }));
        var account = new Account { Email = "user@example.com", PasswordHash = "hash" };
        var token = service.CreateTokens(account).Tokens.AccessToken;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = "viora-tests",
            ValidateAudience = true,
            ValidAudience = "viora-client",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        });

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    private static JsonElement DecodePayload(string token)
    {
        var encoded = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded))).RootElement.Clone();
    }
}
