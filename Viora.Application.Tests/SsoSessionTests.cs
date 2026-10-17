using System.IdentityModel.Tokens.Jwt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Security;
using Viora.Infrastructure.Sso;
using Xunit;

public sealed class SsoSessionTests
{
    [Fact]
    public void AccessTokenCryptographicallyBindsTheReturnedSocialSessionId()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions { Key = new string('x', 40) }));
        var session = Guid.NewGuid(); var tokens = service.CreateTokens(new Account(), session).Tokens;
        var payload = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken).Payload;
        Assert.True(payload.ContainsKey("sid"), string.Join(",", payload.Keys));
        Assert.Equal(tokens.SessionId.ToString(), payload["sid"]);
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(tokens.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "viora-BE", ValidAudience = "viora-client", ValidateIssuer = true, ValidateAudience = true,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 40))),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256], RequireSignedTokens = true, RequireExpirationTime = true,
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        }, out _);
        Assert.Equal("access", principal.FindFirst("token_type")!.Value);
        Assert.Equal(session.ToString(), principal.FindFirst("sid")!.Value);
    }

    [Fact]
    public async Task SsoCredentialsAreFreshHashedAndBoundToTheLiveSocialSession()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", x => x?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "sso@example.test" }; var socialId = Guid.NewGuid();
        db.Accounts.Add(account);
        var sourceExpiry = DateTime.UtcNow.AddHours(1);
        db.RefreshTokens.Add(new RefreshToken { AccountId = account.Id, SessionId = socialId, TokenHash = "test-refresh-hash", ExpiresAt = sourceExpiry });
        await db.SaveChangesAsync();
        var service = new SsoSessionService(db, TimeProvider.System);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.CreateAsync(account.Id, Guid.NewGuid(), default));
        var first = await service.CreateAsync(account.Id, socialId, default);
        // Relational providers may materialize UTC database values as Unspecified.
        // Cookie expiry must keep UTC semantics, including when source expiry wins the cap.
        Assert.Equal(DateTimeKind.Utc, first.ExpiresAt.Kind);
        Assert.Equal(sourceExpiry, first.ExpiresAt);
        var second = await service.CreateAsync(account.Id, socialId, default);
        Assert.NotEqual(first.Credential, second.Credential);
        Assert.DoesNotContain(first.Credential, string.Join(" ", await db.AnktSsoSessions.Select(x => x.TokenHash).ToListAsync()));
        Assert.Equal(account.Id, (await service.AuthenticateAsync(first.Credential, default))!.AccountId);
        await service.RevokeAsync(first.Credential, default);
        Assert.Null(await service.AuthenticateAsync(first.Credential, default));
        Assert.NotNull(await service.AuthenticateAsync(second.Credential, default));
        account.Status = AccountStatus.Banned; await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(second.Credential, default));
        account.Status = AccountStatus.Active; account.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(second.Credential, default));
        account.DeletedAt = null; await db.SaveChangesAsync();
        await db.AnktSsoSessions.ExecuteUpdateAsync(x => x.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Null(await service.AuthenticateAsync(second.Credential, default));
        await db.AnktSsoSessions.ExecuteUpdateAsync(x => x.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddHours(1)));
        await db.RefreshTokens.ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        Assert.Null(await service.AuthenticateAsync(second.Credential, default));
    }
}
