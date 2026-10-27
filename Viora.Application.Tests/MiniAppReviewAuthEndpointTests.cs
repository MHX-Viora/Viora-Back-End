using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Viora.Domain.Entities;
using Viora.Infrastructure.Security;
using viora_BE.Admin;
using viora_BE.Developer;
using Xunit;

public sealed class MiniAppReviewAuthEndpointTests
{
    [Fact]
    public async Task ModulePoliciesRequireBearerAdminAndRecheckLiveRoleAndSession()
    {
        await using var f = await SandboxFixture.Create();
        var admin = new Account { Email = "review-policy@example.test", Role = AccountRole.Admin };
        var sid = Guid.NewGuid();
        var source = new RefreshToken { Account = admin, SessionId = sid, TokenHash = "review-policy-fixture", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1) };
        f.Db.Add(source); await f.Db.SaveChangesAsync();
        var jwt = new JwtOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            Issuer = "https://review-policy.example", Audience = "review-policy-fixture" };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));
        var tokens = new JwtTokenService(Options.Create(jwt));
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(f.Db); builder.Services.AddSingleton<TimeProvider>(f.Clock);
        builder.Services.AddAuthentication().AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new() { ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                IssuerSigningKey = key, RoleClaimType = "role", NameClaimType = "sub", ValidAlgorithms = [SecurityAlgorithms.HmacSha256] };
        });
        builder.Services.AddMiniAppReviewServices();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/read", () => Results.Ok()).RequireAuthorization(MiniAppReviewAuthorization.Read);
        app.MapPost("/decide", () => Results.Ok()).RequireAuthorization(MiniAppReviewAuthorization.Decide);
        app.MapPost("/suspend", () => Results.Ok()).RequireAuthorization(MiniAppReviewAuthorization.Suspend);
        await app.StartAsync(); using var http = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/read")).StatusCode);
        var accessToken = tokens.CreateTokens(admin, sid).Tokens.AccessToken;
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", accessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/read")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/read")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/decide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/suspend", null)).StatusCode);
        admin.Role = AccountRole.Moderator; await f.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/decide", null)).StatusCode);
        var moderatorToken = tokens.CreateTokens(admin, sid).Tokens.AccessToken;
        admin.Role = AccountRole.Admin; await f.Db.SaveChangesAsync();
        http.DefaultRequestHeaders.Authorization = new("Bearer", moderatorToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/read")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", tokens.CreateTokens(admin, Guid.NewGuid()).Tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/read")).StatusCode);
        var missingSid = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer, Audience = jwt.Audience, Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", admin.Id.ToString()), new Claim("role", "2"), new Claim("token_type", "access")])
        });
        http.DefaultRequestHeaders.Authorization = new("Bearer", missingSid);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/read")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        source.RevokedAt = f.Clock.Now.UtcDateTime; await f.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/read")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/suspend", null)).StatusCode);
        Assert.Empty(f.Options.AdminTesterIds);
        Assert.Empty(await f.Db.MiniAppPublications.ToListAsync());
    }
}
