using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence.Repositories;
using viora_BE.Controllers;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class DeveloperGoogleLoginTests
{
    private sealed class VerifiedGoogle(GoogleVerifiedIdentity? identity) : IGoogleIdentityTokenVerifier
    {
        public Task<GoogleVerifiedIdentity?> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(token == "signed-fixture" ? identity : null);
    }
    private sealed class Tokens : ITokenService
    {
        public IssuedAccountTokens CreateTokens(Account account, Guid? sessionId = null) => new(new($"access-{account.Id}", "fixture-refresh", DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddDays(30), sessionId ?? Guid.NewGuid()), "fixture-hash");
        public string HashRefreshToken(string value) => "fixture-hash";
    }
    private static AccountsController Controller(Fixture f, GoogleVerifiedIdentity? identity) => new(null!, new GoogleLoginService(new VerifiedGoogle(identity), new GoogleLoginRepository(f.Db), new Tokens()), null!)
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task DeveloperGoogleRequiresExistingLoginInsteadOfLinkingEmail()
    {
        await using var f = await Fixture.Create();
        var controller = Controller(f, new("new-google-subject", f.Owner.Email!));
        var result = Assert.IsType<ObjectResult>(await controller.DeveloperGoogleLogin(new("signed-fixture"), default));
        Assert.Equal(409, result.StatusCode);
        var error = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("ACCOUNT_LINK_REQUIRED", error.Extensions["code"]);
        Assert.Empty(await f.Db.ExternalLogins.ToListAsync());
        Assert.Empty(await f.Db.RefreshTokens.ToListAsync());
        Assert.Equal(2, await f.Db.Accounts.CountAsync());
        Assert.False(controller.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task NewDeveloperGoogleIdentityCreatesSharedAnktAccountAndSecureRefreshCookie()
    {
        await using var f = await Fixture.Create();
        var controller = Controller(f, new("new-google-subject", "new@example.com", "New partner"));
        var result = Assert.IsType<OkObjectResult>(await controller.DeveloperGoogleLogin(new("signed-fixture"), default));
        Assert.IsType<LoginSuccessResponse>(result.Value);
        var account = await f.Db.Accounts.SingleAsync(a => a.Email == "new@example.com");
        Assert.Null(account.PasswordHash);
        Assert.NotNull(account.EmailVerifiedAt);
        Assert.Equal(account.Id, (await f.Db.ExternalLogins.SingleAsync()).AccountId);
        Assert.Equal(account.Id, (await f.Db.RefreshTokens.SingleAsync()).AccountId);
        var cookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains("path=/api/accounts", cookie);
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie);
    }

    [Fact]
    public async Task ExistingProviderSubjectReusesSameAccountAndDoesNotCreateDuplicate()
    {
        await using var f = await Fixture.Create();
        f.Db.ExternalLogins.Add(new ExternalLogin { AccountId = f.Owner.Id, Provider = "google.com", ProviderSubject = "linked-subject" });
        await f.Db.SaveChangesAsync();
        var controller = Controller(f, new("linked-subject", f.Owner.Email!));
        var result = Assert.IsType<OkObjectResult>(await controller.DeveloperGoogleLogin(new("signed-fixture"), default));
        Assert.Contains(f.Owner.Id.ToString(), Assert.IsType<LoginSuccessResponse>(result.Value).AccessToken);
        Assert.Equal(2, await f.Db.Accounts.CountAsync()); Assert.Single(await f.Db.ExternalLogins.ToListAsync());
    }

    [Fact]
    public async Task ExistingAnktGoogleRouteRetainsItsCompatibilityPolicy()
    {
        await using var f = await Fixture.Create();
        var controller = Controller(f, new("legacy-new-subject", f.Owner.Email!));
        Assert.IsType<OkObjectResult>(await controller.GoogleLogin(new("signed-fixture"), default));
        Assert.Equal(f.Owner.Id, (await f.Db.ExternalLogins.SingleAsync()).AccountId);
    }

    [Fact]
    public async Task InvalidGoogleTokenCannotCreateDeveloperAccountOrCookie()
    {
        await using var f = await Fixture.Create();
        var controller = Controller(f, null);
        var result = Assert.IsType<ObjectResult>(await controller.DeveloperGoogleLogin(new("invalid-token"), default));
        Assert.Equal(401, result.StatusCode); Assert.Empty(await f.Db.ExternalLogins.ToListAsync());
        Assert.False(controller.Response.Headers.ContainsKey("Set-Cookie"));
    }
}
