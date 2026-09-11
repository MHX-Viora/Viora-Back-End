using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Accounts;

public sealed class AccountTokenLifecycleTests
{
    [Fact]
    public async Task Login_creates_an_independent_session_token()
    {
        var account = ActiveAccount();
        var repository = new FakeAccountRepository(account);
        var service = new AccountService(repository, new FakePasswordHasher(), new FakeTokenService());

        var result = await service.LoginAsync(new LoginAccountCommand(account.Email!, "password"), default);

        Assert.Equal(LoginOutcome.Active, result.Outcome);
        Assert.NotEqual(Guid.Empty, result.Tokens!.SessionId);
        Assert.Equal(result.Tokens.SessionId, repository.Tokens.Single().SessionId);
        Assert.Equal(result.Tokens.RefreshTokenExpiresAt, repository.Tokens.Single().ExpiresAt);
    }

    [Fact]
    public async Task Refresh_revokes_the_old_row_and_inserts_a_replacement_in_the_same_session()
    {
        var account = ActiveAccount();
        var sessionId = Guid.NewGuid();
        var old = ActiveRefreshToken(account, sessionId, "old-hash");
        var repository = new FakeAccountRepository(account, old);
        var service = new AccountService(repository, new FakePasswordHasher(), new FakeTokenService());

        var result = await service.RefreshTokenAsync(new RefreshAccountTokenCommand("old"), default);

        Assert.Equal(RefreshTokenOutcome.Active, result.Outcome);
        Assert.Equal(sessionId, result.Tokens!.SessionId);
        Assert.NotNull(old.RevokedAt);
        Assert.NotNull(old.ReplacedByTokenId);
        Assert.Equal(sessionId, repository.Tokens.Single(token => token.Id == old.ReplacedByTokenId).SessionId);
        Assert.Single(repository.Tokens, token => token.SessionId == sessionId && token.RevokedAt is null);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_only_its_session()
    {
        var account = ActiveAccount();
        var compromisedSession = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        var old = ActiveRefreshToken(account, compromisedSession, "old-hash");
        old.RevokedAt = DateTime.UtcNow.AddMinutes(-1);
        old.ReplacedByTokenId = Guid.NewGuid();
        var replacement = ActiveRefreshToken(account, compromisedSession, "replacement-hash");
        replacement.Id = old.ReplacedByTokenId.Value;
        var other = ActiveRefreshToken(account, otherSession, "other-hash");
        var repository = new FakeAccountRepository(account, old, replacement, other);
        var service = new AccountService(repository, new FakePasswordHasher(), new FakeTokenService());

        var result = await service.RefreshTokenAsync(new RefreshAccountTokenCommand("old"), default);

        Assert.Equal(RefreshTokenOutcome.Reused, result.Outcome);
        Assert.NotNull(replacement.RevokedAt);
        Assert.Null(other.RevokedAt);
    }

    [Fact]
    public async Task Logout_without_a_refresh_token_does_not_revoke_other_devices()
    {
        var account = ActiveAccount();
        var first = ActiveRefreshToken(account, Guid.NewGuid(), "first-hash");
        var second = ActiveRefreshToken(account, Guid.NewGuid(), "second-hash");
        var repository = new FakeAccountRepository(account, first, second);
        var service = new AccountService(repository, new FakePasswordHasher(), new FakeTokenService());

        await service.LogoutAsync(new LogoutAccountCommand(null, account.Id), default);

        Assert.Null(first.RevokedAt);
        Assert.Null(second.RevokedAt);
    }

    [Fact]
    public async Task A_second_refresh_uses_the_rotated_token_and_keeps_one_active_row()
    {
        var account = ActiveAccount();
        var sessionId = Guid.NewGuid();
        var repository = new FakeAccountRepository(
            account,
            ActiveRefreshToken(account, sessionId, "old-hash"));
        var service = new AccountService(repository, new FakePasswordHasher(), new FakeTokenService());

        var first = await service.RefreshTokenAsync(new RefreshAccountTokenCommand("old"), default);
        var second = await service.RefreshTokenAsync(
            new RefreshAccountTokenCommand(first.Tokens!.RefreshToken),
            default);

        Assert.Equal(RefreshTokenOutcome.Active, second.Outcome);
        Assert.Equal(sessionId, second.Tokens!.SessionId);
        Assert.Single(repository.Tokens, token => token.SessionId == sessionId && token.RevokedAt is null);
    }

    [Fact]
    public async Task Logout_with_a_refresh_token_revokes_only_the_matching_device_session()
    {
        var account = ActiveAccount();
        var first = ActiveRefreshToken(account, Guid.NewGuid(), "first-hash");
        var second = ActiveRefreshToken(account, Guid.NewGuid(), "second-hash");
        var repository = new FakeAccountRepository(account, first, second);
        var service = new AccountService(repository, new FakePasswordHasher(), new FakeTokenService());

        await service.LogoutAsync(new LogoutAccountCommand("first", account.Id), default);

        Assert.NotNull(first.RevokedAt);
        Assert.Null(second.RevokedAt);
    }

    private static Account ActiveAccount() => new()
    {
        Email = "person@example.com",
        PasswordHash = "hashed",
        Status = AccountStatus.Active
    };

    private static RefreshToken ActiveRefreshToken(Account account, Guid sessionId, string hash) => new()
    {
        Account = account,
        AccountId = account.Id,
        SessionId = sessionId,
        TokenHash = hash,
        ExpiresAt = DateTime.UtcNow.AddDays(1)
    };

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed";
        public bool Verify(string password, string passwordHash) => true;
    }

    private sealed class FakeTokenService : ITokenService
    {
        private int sequence;

        public IssuedAccountTokens CreateTokens(Account account, Guid? sessionId = null)
        {
            var number = ++sequence;
            var accessExpiresAt = DateTime.UtcNow.AddMinutes(15);
            var refreshExpiresAt = DateTime.UtcNow.AddDays(30);
            return new IssuedAccountTokens(
                new AccountTokens(
                    $"access-{number}",
                    $"refresh-{number}",
                    accessExpiresAt,
                    refreshExpiresAt,
                    sessionId ?? Guid.NewGuid()),
                $"refresh-{number}-hash");
        }

        public string HashRefreshToken(string refreshToken) => $"{refreshToken}-hash";
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly Account account;
        public List<RefreshToken> Tokens { get; }

        public FakeAccountRepository(Account account, params RefreshToken[] tokens)
        {
            this.account = account;
            Tokens = [.. tokens];
        }

        public Task<(IReadOnlyList<Account> Items, int Total)> ListAsync(int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult(((IReadOnlyList<Account>)[account], 1));
        public Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Account?>(account);
        public Task<Account?> FindByIdentifierAsync(string? email, string? phone, CancellationToken cancellationToken) => Task.FromResult<Account?>(account);
        public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Tokens.SingleOrDefault(token => token.TokenHash == tokenHash));
        public Task<bool> EmailExistsAsync(string email, Guid? excludingId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> PhoneExistsAsync(string phone, Guid? excludingId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddAsync(Account account, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
        {
            Tokens.Add(refreshToken);
            return Task.CompletedTask;
        }
        public Task<bool> RotateRefreshTokenAsync(Guid currentTokenId, RefreshToken replacement, DateTime revokedAt, CancellationToken cancellationToken)
        {
            var current = Tokens.Single(token => token.Id == currentTokenId);
            if (current.RevokedAt is not null || current.ExpiresAt <= revokedAt) return Task.FromResult(false);
            current.RevokedAt = revokedAt;
            current.ReplacedByTokenId = replacement.Id;
            Tokens.Add(replacement);
            return Task.FromResult(true);
        }
        public Task RevokeRefreshTokenAsync(string tokenHash, Guid accountId, DateTime revokedAt, CancellationToken cancellationToken)
        {
            var token = Tokens.SingleOrDefault(candidate => candidate.TokenHash == tokenHash && candidate.AccountId == accountId);
            if (token is not null) token.RevokedAt = revokedAt;
            return Task.CompletedTask;
        }
        public Task RevokeRefreshTokensForSessionAsync(Guid sessionId, DateTime revokedAt, CancellationToken cancellationToken)
        {
            foreach (var token in Tokens.Where(candidate => candidate.SessionId == sessionId && candidate.RevokedAt is null)) token.RevokedAt = revokedAt;
            return Task.CompletedTask;
        }
        public Task RevokeRefreshTokensForAccountAsync(Guid accountId, DateTime revokedAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> DeleteRetainedRefreshTokensAsync(DateTime cutoff, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task ChangePasswordAndRevokeRefreshTokensAsync(Account account, string passwordHash, DateTime changedAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
