using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Accounts;

public sealed class GoogleLoginServiceTests
{
    [Fact]
    public async Task Login_rejects_an_invalid_or_non_google_token()
    {
        var service = CreateService(identity: null);

        var result = await service.LoginAsync(new GoogleLoginCommand("bad-token"), default);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task Login_issues_viora_tokens_for_a_verified_google_identity()
    {
        var account = ActiveAccount();
        var repository = new FakeGoogleLoginRepository(account);
        var service = CreateService(
            new GoogleVerifiedIdentity("firebase-uid", "person@example.com"),
            repository);

        var result = await service.LoginAsync(new GoogleLoginCommand("valid-token"), default);

        Assert.Equal(LoginOutcome.Active, result.Outcome);
        Assert.Equal("access", result.Tokens?.AccessToken);
        Assert.True(repository.CompletedLogin);
    }

    [Theory]
    [InlineData(AccountStatus.Banned, LoginOutcome.Banned)]
    [InlineData(AccountStatus.Deleted, LoginOutcome.Deleted)]
    public async Task Login_does_not_issue_tokens_for_blocked_accounts(
        AccountStatus status,
        LoginOutcome expected)
    {
        var account = ActiveAccount();
        account.Status = status;
        var service = CreateService(
            new GoogleVerifiedIdentity("firebase-uid", "person@example.com"),
            new FakeGoogleLoginRepository(account));

        var result = await service.LoginAsync(new GoogleLoginCommand("valid-token"), default);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Tokens);
    }

    private static GoogleLoginService CreateService(
        GoogleVerifiedIdentity? identity,
        FakeGoogleLoginRepository? repository = null) =>
        new(
            new FakeVerifier(identity),
            repository ?? new FakeGoogleLoginRepository(ActiveAccount()),
            new FakeTokenService());

    private static Account ActiveAccount() => new()
    {
        Email = "person@example.com",
        Status = AccountStatus.Active
    };

    private sealed class FakeVerifier(GoogleVerifiedIdentity? identity)
        : IGoogleIdentityTokenVerifier
    {
        public Task<GoogleVerifiedIdentity?> VerifyAsync(
            string firebaseToken,
            CancellationToken cancellationToken) => Task.FromResult(identity);
    }

    private sealed class FakeGoogleLoginRepository(Account account)
        : IGoogleLoginRepository
    {
        public bool CompletedLogin { get; private set; }

        public Task<Account> ResolveAccountAsync(
            GoogleVerifiedIdentity identity,
            CancellationToken cancellationToken) => Task.FromResult(account);

        public Task CompleteLoginAsync(
            Account resolvedAccount,
            RefreshToken refreshToken,
            DateTime loginAt,
            CancellationToken cancellationToken)
        {
            CompletedLogin = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokenService : ITokenService
    {
        public IssuedAccountTokens CreateTokens(Account account) => new(
            new AccountTokens("access", "refresh"),
            "hash",
            DateTime.UtcNow.AddDays(1));

        public string HashRefreshToken(string refreshToken) => "hash";
    }
}
