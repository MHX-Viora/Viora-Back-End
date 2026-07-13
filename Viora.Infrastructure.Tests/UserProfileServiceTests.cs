using Viora.Application.Users;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class UserProfileServiceTests
{
    [Fact]
    public async Task Create_returns_profile_for_active_account()
    {
        var account = new Account { Status = AccountStatus.Active, Role = AccountRole.User, Email = "user@example.com", PasswordHash = "hash" };
        var repository = new FakeUserProfileRepository(account);
        var service = new UserProfileService(repository);

        var result = await service.CreateAsync(
            account.Id,
            new SaveUserProfileCommand(" Trịnh Trọng Quyền ", "https://cdn.viora.com/avatar.jpg", "https://cdn.viora.com/cover.jpg", Gender.Male),
            CancellationToken.None);

        Assert.Equal(account.Id, result.AccountId);
        Assert.Equal("Trịnh Trọng Quyền", result.DisplayName);
        Assert.Equal(Gender.Male, account.User!.Gender);
        Assert.Equal(AccountRole.User, result.Role);
        Assert.False(result.IsVerified);
        Assert.Equal(UserIdentityState.NotVerified, result.VerificationStatus);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Create_rejects_duplicate_profile_with_vietnamese_message()
    {
        var account = ActiveAccountWithUser();
        var service = new UserProfileService(new FakeUserProfileRepository(account));

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.CreateAsync(
            account.Id,
            new SaveUserProfileCommand("Tên mới", null, null, Gender.Unknown),
            CancellationToken.None));

        Assert.Equal(UserProfileError.ProfileAlreadyExists, exception.Code);
        Assert.Equal("Hồ sơ người dùng đã tồn tại.", exception.Message);
    }

    [Fact]
    public async Task Create_rejects_inactive_account()
    {
        var account = new Account { Status = AccountStatus.Banned, Email = "user@example.com", PasswordHash = "hash" };
        var service = new UserProfileService(new FakeUserProfileRepository(account));

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.CreateAsync(
            account.Id,
            new SaveUserProfileCommand("Tên người dùng", null, null, Gender.Unknown),
            CancellationToken.None));

        Assert.Equal(UserProfileError.AccountUnavailable, exception.Code);
        Assert.Equal("Tài khoản không tồn tại hoặc không ở trạng thái hoạt động.", exception.Message);
    }

    [Fact]
    public async Task Update_changes_existing_profile_and_returns_it()
    {
        var account = ActiveAccountWithUser();
        var repository = new FakeUserProfileRepository(account);
        var service = new UserProfileService(repository);

        var result = await service.UpdateAsync(
            account.Id,
            new SaveUserProfileCommand("Tên cập nhật", null, "https://cdn.viora.com/cover-new.jpg", Gender.Female),
            CancellationToken.None);

        Assert.Equal("Tên cập nhật", result.DisplayName);
        Assert.Null(result.AvatarUrl);
        Assert.Equal(Gender.Female, account.User!.Gender);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Update_rejects_when_profile_does_not_exist()
    {
        var account = new Account { Status = AccountStatus.Active, Email = "user@example.com", PasswordHash = "hash" };
        var service = new UserProfileService(new FakeUserProfileRepository(account));

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.UpdateAsync(
            account.Id,
            new SaveUserProfileCommand("Tên người dùng", null, null, Gender.Unknown),
            CancellationToken.None));

        Assert.Equal(UserProfileError.ProfileNotFound, exception.Code);
        Assert.Equal("Không tìm thấy hồ sơ người dùng.", exception.Message);
    }

    private static Account ActiveAccountWithUser()
    {
        var account = new Account { Status = AccountStatus.Active, Email = "user@example.com", PasswordHash = "hash" };
        account.User = new User { AccountId = account.Id, Account = account, DisplayName = "Tên cũ" };
        return account;
    }

    private sealed class FakeUserProfileRepository(Account? account) : IUserProfileRepository
    {
        public int SaveCount { get; private set; }

        public Task<Account?> GetAccountWithUserAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(account?.Id == accountId ? account : null);

        public Task AddAsync(User user, CancellationToken cancellationToken)
        {
            if (account is not null)
            {
                account.User = user;
            }
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
