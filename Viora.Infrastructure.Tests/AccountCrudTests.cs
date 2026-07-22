using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Viora.Infrastructure.Security;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class AccountCrudTests
{
    [Fact]
    public void Pbkdf2_hasher_never_stores_plaintext_and_can_verify()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var hash = hasher.Hash("Password123");

        Assert.NotEqual("Password123", hash);
        Assert.True(hasher.Verify("Password123", hash));
        Assert.False(hasher.Verify("WrongPassword", hash));
    }

    [Fact]
    public async Task Register_detects_email_normalizes_and_hashes_password()
    {
        var repository = new FakeAccountRepository();
        var service = new AccountService(repository, new FakePasswordHasher());

        var result = await service.RegisterAsync(
            new RegisterAccountCommand("  USER@Example.COM ", "Password123"),
            CancellationToken.None);

        Assert.Equal("user@example.com", result.Email);
        Assert.Null(result.Phone);
        Assert.Equal("hashed:Password123", repository.Accounts.Single().PasswordHash);
        Assert.DoesNotContain("Password", result.ToString());
    }

    [Fact]
    public async Task Register_detects_and_normalizes_phone()
    {
        var repository = new FakeAccountRepository();
        var service = new AccountService(repository, new FakePasswordHasher());

        var result = await service.RegisterAsync(
            new RegisterAccountCommand(" +84 901-234-567 ", "Password123"),
            CancellationToken.None);

        Assert.Null(result.Email);
        Assert.Equal("+84901234567", result.Phone);
    }

    [Fact]
    public async Task Register_rejects_invalid_identifier()
    {
        var service = new AccountService(new FakeAccountRepository(), new FakePasswordHasher());

        var exception = await Assert.ThrowsAsync<AccountValidationException>(() =>
            service.RegisterAsync(new RegisterAccountCommand("not-an-email-or-phone", "Password123"), CancellationToken.None));

        Assert.Equal("INVALID_IDENTIFIER", exception.Code);
    }

    [Fact]
    public async Task Register_rejects_duplicate_email()
    {
        var repository = new FakeAccountRepository
        {
            Accounts = { new Account { Email = "user@example.com", PasswordHash = "hash" } }
        };
        var service = new AccountService(repository, new FakePasswordHasher());

        var exception = await Assert.ThrowsAsync<AccountConflictException>(() =>
            service.RegisterAsync(
                new RegisterAccountCommand("USER@example.com", "Password123"),
                CancellationToken.None));

        Assert.Equal("EMAIL_EXISTS", exception.Code);
    }

    [Fact]
    public async Task List_excludes_deleted_accounts_and_returns_pagination()
    {
        var repository = new FakeAccountRepository
        {
            Accounts =
            {
                new Account { Email = "active@example.com", PasswordHash = "hash" },
                new Account { Email = "deleted@example.com", PasswordHash = "hash", Status = AccountStatus.Deleted, DeletedAt = DateTime.UtcNow }
            }
        };
        var service = new AccountService(repository, new FakePasswordHasher());

        var result = await service.ListAsync(1, 20, CancellationToken.None);

        Assert.Single(result.Data);
        Assert.Equal(1, result.Pagination.TotalItems);
        Assert.Equal("active@example.com", result.Data[0].Email);
    }

    [Fact]
    public async Task Update_returns_null_when_account_does_not_exist()
    {
        var service = new AccountService(new FakeAccountRepository(), new FakePasswordHasher());

        var result = await service.UpdateAsync(
            Guid.NewGuid(),
            new UpdateAccountCommand("user@example.com", null, null),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_soft_deletes_and_is_idempotent()
    {
        var account = new Account { Email = "user@example.com", PasswordHash = "hash" };
        var repository = new FakeAccountRepository { Accounts = { account } };
        var service = new AccountService(repository, new FakePasswordHasher());

        await service.DeleteAsync(account.Id, CancellationToken.None);
        await service.DeleteAsync(account.Id, CancellationToken.None);

        Assert.Equal(AccountStatus.Deleted, account.Status);
        Assert.NotNull(account.DeletedAt);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Login_returns_generic_error_when_account_does_not_exist()
    {
        var result = await CreateLoginService(new FakeAccountRepository()).LoginAsync(
            new LoginAccountCommand("missing@example.com", "Password123"), CancellationToken.None);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Status);
        Assert.Equal("Thông tin đăng nhập hoặc mật khẩu không chính xác.", result.Message);
    }

    [Fact]
    public async Task Login_returns_generic_error_when_password_is_wrong()
    {
        var repository = new FakeAccountRepository
        {
            Accounts = { new Account { Email = "user@example.com", PasswordHash = "hashed:CorrectPassword" } }
        };

        var result = await CreateLoginService(repository).LoginAsync(
            new LoginAccountCommand("USER@example.com", "WrongPassword"), CancellationToken.None);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Status);
    }

    [Theory]
    [InlineData(AccountStatus.Banned, LoginOutcome.Banned, "Tài khoản của bạn đã bị khóa do vi phạm Tiêu chuẩn cộng đồng. Vui lòng liên hệ hỗ trợ nếu cho rằng có sự nhầm lẫn.")]
    [InlineData(AccountStatus.Deleted, LoginOutcome.Deleted, "Tài khoản này không còn tồn tại hoặc đã bị xóa.")]
    public async Task Login_returns_status_and_vietnamese_message_for_unavailable_account(
        AccountStatus status,
        LoginOutcome outcome,
        string message)
    {
        var repository = new FakeAccountRepository
        {
            Accounts = { new Account { Phone = "0901234567", PasswordHash = "hashed:Password123", Status = status } }
        };

        var result = await CreateLoginService(repository).LoginAsync(
            new LoginAccountCommand("090 123-4567", "Password123"), CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(status, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task Login_active_account_returns_tokens_and_nullable_user()
    {
        var account = new Account { Email = "user@example.com", PasswordHash = "hashed:Password123" };
        var repository = new FakeAccountRepository { Accounts = { account } };

        var result = await CreateLoginService(repository).LoginAsync(
            new LoginAccountCommand("user@example.com", "Password123"), CancellationToken.None);

        Assert.Equal(LoginOutcome.Active, result.Outcome);
        Assert.Equal(AccountStatus.Active, result.Status);
        Assert.Equal(new AccountTokens("access-token", "refresh-token"), result.Tokens);
        Assert.Null(result.User);
        Assert.NotNull(account.LastLoginAt);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Login_active_account_maps_user_profile_when_it_exists()
    {
        var account = new Account
        {
            Email = "user@example.com",
            PasswordHash = "hashed:Password123",
            Role = AccountRole.Moderator
        };
        account.User = new User
        {
            AccountId = account.Id,
            DisplayName = "Trịnh Trọng Quyền",
            AvatarUrl = "https://example.com/avatar.jpg",
            CoverUrl = "https://example.com/cover.jpg",
            IsVerified = true,
            IdentityStatus = UserIdentityState.Verified,
            Account = account
        };
        var repository = new FakeAccountRepository { Accounts = { account } };

        var result = await CreateLoginService(repository).LoginAsync(
            new LoginAccountCommand("user@example.com", "Password123"), CancellationToken.None);

        Assert.NotNull(result.User);
        Assert.Equal(account.User.Id, result.User.Id);
        Assert.Equal(account.Id, result.User.AccountId);
        Assert.Equal("Trịnh Trọng Quyền", result.User.DisplayName);
        Assert.Equal(AccountRole.Moderator, result.User.Role);
        Assert.True(result.User.IsVerified);
        Assert.Equal(UserIdentityState.Verified, result.User.VerificationStatus);
    }

    [Fact]
    public async Task Refresh_token_rotates_once_and_reuse_is_rejected()
    {
        var account = new Account { Email = "user@example.com", PasswordHash = "hash" };
        var current = new RefreshToken
        {
            AccountId = account.Id,
            Account = account,
            TokenHash = "hash:valid-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        var repository = new FakeAccountRepository { Accounts = { account }, RefreshTokens = { current } };
        var service = CreateLoginService(repository);

        var first = await service.RefreshTokenAsync(
            new RefreshAccountTokenCommand("valid-refresh-token"), CancellationToken.None);
        var reused = await service.RefreshTokenAsync(
            new RefreshAccountTokenCommand("valid-refresh-token"), CancellationToken.None);

        Assert.Equal(RefreshTokenOutcome.Active, first.Outcome);
        Assert.Equal(new AccountTokens("access-token", "refresh-token"), first.Tokens);
        Assert.Single(repository.RefreshTokens);
        Assert.Equal("refresh-token-hash", current.TokenHash);
        Assert.Null(current.RevokedAt);
        Assert.Equal(RefreshTokenOutcome.Invalid, reused.Outcome);
        Assert.Null(reused.Tokens);
    }

    [Fact]
    public async Task Refresh_token_rejects_expired_token()
    {
        var account = new Account { Email = "user@example.com", PasswordHash = "hash" };
        var repository = new FakeAccountRepository { Accounts = { account } };
        repository.RefreshTokens.Add(new RefreshToken
        {
            AccountId = account.Id,
            Account = account,
            TokenHash = "hash:expired-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });

        var result = await CreateLoginService(repository).RefreshTokenAsync(
            new RefreshAccountTokenCommand("expired-token"), CancellationToken.None);

        Assert.Equal(RefreshTokenOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Logout_revokes_matching_refresh_token_and_is_idempotent()
    {
        var account = new Account { Email = "user@example.com", PasswordHash = "hash" };
        var current = new RefreshToken
        {
            AccountId = account.Id,
            Account = account,
            TokenHash = "hash:valid-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        var repository = new FakeAccountRepository { Accounts = { account }, RefreshTokens = { current } };
        var service = CreateLoginService(repository);

        await service.LogoutAsync(new LogoutAccountCommand("valid-refresh-token", account.Id), CancellationToken.None);
        await service.LogoutAsync(new LogoutAccountCommand("valid-refresh-token", account.Id), CancellationToken.None);

        Assert.NotNull(current.RevokedAt);
    }

    [Fact]
    public async Task Logout_without_refresh_token_revokes_all_account_refresh_tokens()
    {
        var account = new Account { Email = "user@example.com", PasswordHash = "hash" };
        var other = new Account { Email = "other@example.com", PasswordHash = "hash" };
        var ownToken = new RefreshToken
        {
            AccountId = account.Id,
            Account = account,
            TokenHash = "hash:own-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        var otherToken = new RefreshToken
        {
            AccountId = other.Id,
            Account = other,
            TokenHash = "hash:other-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        var repository = new FakeAccountRepository
        {
            Accounts = { account, other },
            RefreshTokens = { ownToken, otherToken }
        };
        var service = CreateLoginService(repository);

        await service.LogoutAsync(new LogoutAccountCommand(null, account.Id), CancellationToken.None);

        Assert.NotNull(ownToken.RevokedAt);
        Assert.Null(otherToken.RevokedAt);
    }

    private static AccountService CreateLoginService(FakeAccountRepository repository) =>
        new(repository, new FakePasswordHasher(), new FakeTokenService());

    private sealed class FakeTokenService : ITokenService
    {
        public IssuedAccountTokens CreateTokens(Account account) => new(
            new AccountTokens("access-token", "refresh-token"),
            "refresh-token-hash",
            DateTime.UtcNow.AddDays(30));

        public string HashRefreshToken(string refreshToken) => $"hash:{refreshToken}";
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        public List<Account> Accounts { get; } = [];
        public List<RefreshToken> RefreshTokens { get; } = [];
        public int SaveCount { get; private set; }

        public Task AddAsync(Account account, CancellationToken cancellationToken)
        {
            Accounts.Add(account);
            return Task.CompletedTask;
        }

        public Task<bool> EmailExistsAsync(string email, Guid? excludingId, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.Any(x => x.Id != excludingId && x.Email == email));

        public Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.SingleOrDefault(x => x.Id == id));

        public Task<Account?> FindByIdentifierAsync(string? email, string? phone, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.SingleOrDefault(x => email is not null ? x.Email == email : x.Phone == phone));

        public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(RefreshTokens.SingleOrDefault(x => x.TokenHash == tokenHash));

        public Task<(IReadOnlyList<Account> Items, int Total)> ListAsync(int skip, int take, CancellationToken cancellationToken)
        {
            var active = Accounts.Where(x => x.DeletedAt is null).OrderByDescending(x => x.CreatedAt).ToList();
            return Task.FromResult<(IReadOnlyList<Account>, int)>((active.Skip(skip).Take(take).ToList(), active.Count));
        }

        public Task<bool> PhoneExistsAsync(string phone, Guid? excludingId, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.Any(x => x.Id != excludingId && x.Phone == phone));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
        {
            RefreshTokens.Add(refreshToken);
            return Task.CompletedTask;
        }

        public Task<bool> RotateRefreshTokenAsync(
            Guid currentTokenId,
            RefreshToken replacement,
            DateTime revokedAt,
            CancellationToken cancellationToken)
        {
            var current = RefreshTokens.SingleOrDefault(x => x.Id == currentTokenId);
            if (current is null || current.RevokedAt is not null || current.ExpiresAt <= revokedAt)
            {
                return Task.FromResult(false);
            }

            current.TokenHash = replacement.TokenHash;
            current.ExpiresAt = replacement.ExpiresAt;
            current.RevokedAt = null;
            current.ReplacedByTokenHash = null;
            return Task.FromResult(true);
        }

        public Task RevokeRefreshTokenAsync(string tokenHash, Guid accountId, DateTime revokedAt, CancellationToken cancellationToken)
        {
            var current = RefreshTokens.SingleOrDefault(x => x.TokenHash == tokenHash && x.AccountId == accountId);
            if (current is not null && current.RevokedAt is null && current.ExpiresAt > revokedAt)
            {
                current.RevokedAt = revokedAt;
            }

            return Task.CompletedTask;
        }

        public Task RevokeRefreshTokensForAccountAsync(Guid accountId, DateTime revokedAt, CancellationToken cancellationToken)
        {
            foreach (var token in RefreshTokens.Where(x =>
                x.AccountId == accountId &&
                x.RevokedAt is null &&
                x.ExpiresAt > revokedAt))
            {
                token.RevokedAt = revokedAt;
            }

            return Task.CompletedTask;
        }
    }
}
