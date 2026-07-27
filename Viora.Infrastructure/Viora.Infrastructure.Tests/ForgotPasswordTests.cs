using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ForgotPasswordTests
{
    [Theory]
    [InlineData("0901234567", "+84901234567")]
    [InlineData("+84901234567", "+84901234567")]
    [InlineData("+84 901-234-567", "+84901234567")]
    public void Phone_number_is_normalized_to_e164(string input, string expected) =>
        Assert.Equal(expected, ForgotPasswordIdentifier.NormalizePhone(input));

    [Fact]
    public async Task Set_phone_rejects_firebase_token_for_a_different_phone()
    {
        var account = Account();
        var repository = new FakeRepository(account);
        var handler = new SetForgotPasswordPhoneHandler(
            repository,
            new FakeFirebaseVerifier(phoneNumber: "+84909999999"),
            new SetForgotPasswordPhoneValidator());

        var result = await handler.Handle(
            new(account.Id, "+84901234567", "firebase-token"),
            CancellationToken.None);

        Assert.Equal(ForgotPasswordOutcome.Unauthorized, result.Outcome);
        Assert.Null(account.Phone);
    }

    [Fact]
    public async Task Reset_changes_password_and_revokes_refresh_tokens()
    {
        var account = Account(phone: "+84901234567", passwordHash: "old-hash");
        var repository = new FakeRepository(account);
        var handler = new ResetForgottenPasswordHandler(
            repository,
            new FakeFirebaseVerifier(phoneNumber: "+84901234567"),
            new FakePasswordHasher(),
            new FakeResetHasher(),
            new ResetForgottenPasswordValidator());

        var result = await handler.Handle(
            new("firebase-token", "NewPassword1", "+84901234567"),
            CancellationToken.None);

        Assert.Equal(ForgotPasswordOutcome.Success, result.Outcome);
        Assert.Equal("bcrypt:NewPassword1", account.PasswordHash);
        Assert.True(repository.RefreshTokensRevoked);
    }

    [Fact]
    public async Task Reset_rejects_the_current_password()
    {
        var account = Account(phone: "+84901234567", passwordHash: "hashed:SamePassword1");
        var repository = new FakeRepository(account);
        var handler = new ResetForgottenPasswordHandler(
            repository,
            new FakeFirebaseVerifier(phoneNumber: "+84901234567"),
            new FakePasswordHasher(),
            new FakeResetHasher(),
            new ResetForgottenPasswordValidator());

        var result = await handler.Handle(
            new("firebase-token", "SamePassword1", "+84901234567"),
            CancellationToken.None);

        Assert.Equal(ForgotPasswordOutcome.SamePassword, result.Outcome);
        Assert.False(repository.RefreshTokensRevoked);
    }

    [Fact]
    public async Task Reset_finds_email_account_from_verified_firebase_identity()
    {
        var account = Account(passwordHash: "old-hash");
        var repository = new FakeRepository(account);
        var handler = new ResetForgottenPasswordHandler(
            repository,
            new FakeFirebaseVerifier(email: "user@example.com"),
            new FakePasswordHasher(),
            new FakeResetHasher(),
            new ResetForgottenPasswordValidator());

        var result = await handler.Handle(
            new("firebase-token", "NewPassword1", "user@example.com"),
            CancellationToken.None);

        Assert.Equal(ForgotPasswordOutcome.Success, result.Outcome);
        Assert.Equal("bcrypt:NewPassword1", account.PasswordHash);
    }

    [Fact]
    public async Task Reset_rejects_identity_that_does_not_match_requested_email()
    {
        var account = Account(passwordHash: "old-hash");
        var repository = new FakeRepository(account);
        var handler = new ResetForgottenPasswordHandler(
            repository,
            new FakeFirebaseVerifier(email: "attacker@example.com"),
            new FakePasswordHasher(),
            new FakeResetHasher(),
            new ResetForgottenPasswordValidator());

        var result = await handler.Handle(
            new("firebase-token", "NewPassword1", "user@example.com"),
            CancellationToken.None);

        Assert.Equal(ForgotPasswordOutcome.Unauthorized, result.Outcome);
        Assert.Equal("old-hash", account.PasswordHash);
        Assert.False(repository.RefreshTokensRevoked);
    }

    private static Account Account(string? phone = null, string passwordHash = "old-hash") =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            Phone = phone,
            PasswordHash = passwordHash,
            Status = AccountStatus.Active
        };

    private sealed class FakeRepository(Account account) : IForgotPasswordRepository
    {
        public bool RefreshTokensRevoked { get; private set; }

        public Task<Account?> FindByIdentifierAsync(
            string? email,
            IReadOnlyList<string> phoneCandidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(account);

        public Task<Account?> FindByPhoneAsync(
            IReadOnlyList<string> phoneCandidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(
                account.Phone is not null && phoneCandidates.Contains(account.Phone)
                    ? account
                    : null);

        public Task<Account?> FindByEmailAsync(
            string email,
            CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(
                string.Equals(account.Email, email, StringComparison.Ordinal)
                    ? account
                    : null);

        public Task<Account?> GetAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(account.Id == accountId ? account : null);

        public Task<bool> PhoneExistsAsync(
            string phoneNumber,
            Guid excludingAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task SavePhoneAsync(
            Account target,
            string phoneNumber,
            CancellationToken cancellationToken)
        {
            target.Phone = phoneNumber;
            return Task.CompletedTask;
        }

        public Task ChangePasswordAndRevokeRefreshTokensAsync(
            Account target,
            string passwordHash,
            DateTime changedAt,
            CancellationToken cancellationToken)
        {
            target.PasswordHash = passwordHash;
            RefreshTokensRevoked = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFirebaseVerifier(
        string? email = null,
        string? phoneNumber = null) : IFirebaseIdentityTokenVerifier
    {
        public Task<FirebaseVerifiedIdentity?> VerifyAsync(
            string firebaseToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<FirebaseVerifiedIdentity?>(
                new(email, phoneNumber));
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string passwordHash) =>
            passwordHash == $"hashed:{password}";
    }

    private sealed class FakeResetHasher : IPasswordResetHasher
    {
        public string Hash(string password) => $"bcrypt:{password}";
    }
}
