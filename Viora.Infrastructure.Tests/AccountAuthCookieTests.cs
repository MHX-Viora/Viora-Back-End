using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Accounts;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class AccountAuthCookieTests
{
    [Fact]
    public async Task Login_writes_refresh_token_to_secure_http_only_cookie()
    {
        var service = new FakeAccountService
        {
            LoginResult = new LoginAccountResult(
                LoginOutcome.Active,
                AccountStatus.Active,
                null,
                new AccountTokens("access-token", "refresh-token"),
                null)
        };
        var controller = CreateController(service);

        var result = await controller.Login(
            new LoginAccountRequest("user@example.com", "Password123"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var cookie = Assert.Single(controller.Response.Headers.SetCookie);
        Assert.Contains("refreshToken=refresh-token", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_reads_and_rotates_cookie_without_exposing_refresh_token()
    {
        var service = new FakeAccountService
        {
            RefreshResult = new RefreshAccountTokenResult(
                RefreshTokenOutcome.Active,
                new AccountTokens("new-access-token", "new-refresh-token"),
                null)
        };
        var controller = CreateController(service);
        controller.Request.Headers.Cookie = "refreshToken=old-refresh-token";

        var result = await controller.RefreshToken(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RefreshTokenSuccessResponse>(ok.Value);
        Assert.Equal("new-access-token", response.AccessToken);
        Assert.Equal("old-refresh-token", service.ReceivedRefreshToken);
        Assert.Contains("refreshToken=new-refresh-token", Assert.Single(controller.Response.Headers.SetCookie));
    }

    private static AccountsController CreateController(IAccountService service) => new(service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private sealed class FakeAccountService : IAccountService
    {
        public LoginAccountResult LoginResult { get; init; } = null!;
        public RefreshAccountTokenResult RefreshResult { get; init; } = null!;
        public string? ReceivedRefreshToken { get; private set; }

        public Task<LoginAccountResult> LoginAsync(LoginAccountCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(LoginResult);

        public Task<RefreshAccountTokenResult> RefreshTokenAsync(RefreshAccountTokenCommand command, CancellationToken cancellationToken)
        {
            ReceivedRefreshToken = command.RefreshToken;
            return Task.FromResult(RefreshResult);
        }

        public Task<PagedAccountResponse> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AccountResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AccountResponse> RegisterAsync(RegisterAccountCommand command, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AccountResponse?> UpdateAsync(Guid id, UpdateAccountCommand command, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
