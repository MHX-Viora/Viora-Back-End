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

    [Fact]
    public async Task Login_repairs_an_email_display_name_from_verified_google_name()
    {
        var account = ActiveAccount();
        account.User = new User
        {
            Account = account,
            AccountId = account.Id,
            DisplayName = "person@example.com"
        };
        var service = CreateService(
            new GoogleVerifiedIdentity(
                "firebase-uid",
                "person@example.com",
                "Nguyen Van An"),
            new FakeGoogleLoginRepository(account));

        var result = await service.LoginAsync(new GoogleLoginCommand("valid-token"), default);

        Assert.Equal("Nguyen Van An", result.User?.DisplayName);
    }

    [Fact]
    public async Task Login_preserves_an_existing_non_email_display_name()
    {
        var account = ActiveAccount();
        account.User = new User
        {
            Account = account,
            AccountId = account.Id,
            DisplayName = "Ten hien tai"
        };
        var service = CreateService(
            new GoogleVerifiedIdentity(
                "firebase-uid",
                "person@example.com",
                "Google Name"),
            new FakeGoogleLoginRepository(account));

        var result = await service.LoginAsync(new GoogleLoginCommand("valid-token"), default);

        Assert.Equal("Ten hien tai", result.User?.DisplayName);
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
        public IssuedAccountTokens CreateTokens(Account account, Guid? sessionId = null) => new(
            new AccountTokens(
                "access",
                "refresh",
                DateTime.UtcNow.AddMinutes(15),
                DateTime.UtcNow.AddDays(1),
                sessionId ?? Guid.NewGuid()),
            "hash");

        public string HashRefreshToken(string refreshToken) => "hash";
    }
}
