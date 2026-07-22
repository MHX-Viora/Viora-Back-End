using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using System.Security.Claims;
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
        Assert.Contains("path=/api/accounts", cookie, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task Logout_revokes_cookie_refresh_token_and_clears_cookie()
    {
        var service = new FakeAccountService();
        var controller = CreateController(service);
        var accountId = Guid.NewGuid();
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", accountId.ToString())],
            "Bearer"));
        controller.Request.Headers.Cookie = "refreshToken=refresh-token";

        var result = await controller.Logout(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("refresh-token", service.ReceivedLogoutRefreshToken);
        Assert.Equal(accountId, service.ReceivedLogoutAccountId);
        var cookie = Assert.Single(controller.Response.Headers.SetCookie);
        Assert.Contains("refreshToken=", cookie);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static AccountsController CreateController(IAccountService service) => new(service, new FakeSender())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private sealed class FakeAccountService : IAccountService
    {
        public LoginAccountResult LoginResult { get; init; } = null!;
        public RefreshAccountTokenResult RefreshResult { get; init; } = null!;
        public string? ReceivedRefreshToken { get; private set; }
        public string? ReceivedLogoutRefreshToken { get; private set; }
        public Guid? ReceivedLogoutAccountId { get; private set; }

        public Task<LoginAccountResult> LoginAsync(LoginAccountCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(LoginResult);

        public Task<RefreshAccountTokenResult> RefreshTokenAsync(RefreshAccountTokenCommand command, CancellationToken cancellationToken)
        {
            ReceivedRefreshToken = command.RefreshToken;
            return Task.FromResult(RefreshResult);
        }

        public Task LogoutAsync(LogoutAccountCommand command, CancellationToken cancellationToken)
        {
            ReceivedLogoutRefreshToken = command.RefreshToken;
            ReceivedLogoutAccountId = command.AccountId;
            return Task.CompletedTask;
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

        public Task<ChangePasswordResult> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class FakeSender : ISender
    {
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotImplementedException();

        public Task<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse> =>
            throw new NotImplementedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
