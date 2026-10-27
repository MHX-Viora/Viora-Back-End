using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionAuthEndpointTests
{
    private const string Key = "isolated-production-auth-fixture-signing-key";
    [Fact]
    public async Task RealSignedBearerRequiresLiveSourceAndNeverFallsBackToAnonymous()
    {
        await using var f = await SandboxFixture.Create();
        var account = new Account { Email = "host-auth@example.test" }; var sid = Guid.NewGuid();
        f.Db.AddRange(account, new RefreshToken { AccountId = account.Id, SessionId = sid, TokenHash = "host-auth-fixture", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await f.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new() { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                ValidIssuer = "host-auth-fixture", ValidAudience = "host-auth-fixture", ValidateIssuer = true, ValidateAudience = true,
                RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero };
        });
        await using var app = builder.Build(); app.UseAuthentication();
        app.MapPost("/probe", async (Microsoft.AspNetCore.Http.HttpContext http) =>
        {
            try
            {
                MiniAppProductionAuthentication.RequireWebOrigin(http, new() { FirstPartyOrigin = "https://app.example.com" });
                var actor = await MiniAppProductionAuthentication.ResolveAsync(http, new(f.Db, f.Clock), default);
                return Microsoft.AspNetCore.Http.Results.Ok(new { authenticated = actor.AccountId != null });
            }
            catch (DeveloperAuthException e) { return Microsoft.AspNetCore.Http.Results.StatusCode(e.Status); }
        });
        await app.StartAsync(); using var http = app.GetTestClient(); http.BaseAddress = new("https://api.example.com");
        http.DefaultRequestHeaders.Add("Origin", "https://app.example.com");
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/probe", null)).StatusCode);
        foreach (var token in new[] { "invalid", Token(account.Id, sid, key: Key + "wrong"), Token(account.Id, sid, type: "refresh"),
            Token(account.Id, sid, expired: true), Token(account.Id, Guid.NewGuid()), Token(Guid.NewGuid(), sid) })
        {
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/probe", null)).StatusCode);
        }
        http.DefaultRequestHeaders.Authorization = new("Bearer", Token(account.Id, sid));
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/probe", null)).StatusCode);
        await f.Db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/probe", null)).StatusCode);
        http.DefaultRequestHeaders.Authorization = null;
        http.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/probe", null)).StatusCode);
        http.DefaultRequestHeaders.Add("Origin", "https://app.example.com.evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/probe", null)).StatusCode);

    }
    private static string Token(Guid account, Guid sid, string type = "access", string key = Key, bool expired = false) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("host-auth-fixture", "host-auth-fixture",
            [new Claim("sub", account.ToString()), new Claim("sid", sid.ToString()), new Claim("token_type", type)],
            expires: DateTime.UtcNow.AddMinutes(expired ? -1 : 5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
}
