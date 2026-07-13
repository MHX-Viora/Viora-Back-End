using Viora.Application.Users;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class UserProfileServiceTests
{
    [Fact]
    public async Task Create_uploads_images_then_persists_provider_urls()
    {
        var account = ActiveAccount();
        var repository = new FakeUserProfileRepository(account);
        var storage = new FakeProfileImageStorage();
        var service = new UserProfileService(repository, storage);
        await using var avatar = new MemoryStream([1, 2, 3]);
        await using var cover = new MemoryStream([4, 5, 6]);

        var result = await service.CreateAsync(
            account.Id,
            new SaveUserProfileCommand(
                " Display name ",
                Gender.Male,
                new ProfileImageFile(avatar, "avatar.jpg"),
                new ProfileImageFile(cover, "cover.jpg")),
            CancellationToken.None);

        Assert.Equal("Display name", result.DisplayName);
        Assert.Equal(Gender.Male, result.Gender);
        Assert.Equal("https://res.cloudinary.com/viora/avatar.jpg", result.AvatarUrl);
        Assert.Equal("https://res.cloudinary.com/viora/cover.jpg", result.CoverUrl);
        Assert.Equal(["avatar", "cover"], storage.Uploads.Select(x => x.PublicId));
        Assert.All(storage.Uploads, upload =>
            Assert.Equal($"viora/users/{account.Id:N}/profile", upload.Folder));
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Create_rejects_duplicate_profile_before_upload()
    {
        var account = ActiveAccountWithUser();
        var storage = new FakeProfileImageStorage();
        var service = new UserProfileService(new FakeUserProfileRepository(account), storage);
        await using var avatar = new MemoryStream([1]);

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.CreateAsync(
            account.Id,
            new SaveUserProfileCommand("New name", Gender.Unknown, new ProfileImageFile(avatar, "a.jpg"), null),
            CancellationToken.None));

        Assert.Equal(UserProfileError.ProfileAlreadyExists, exception.Code);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task Create_rejects_inactive_account()
    {
        var account = new Account { Status = AccountStatus.Banned, Email = "user@example.com", PasswordHash = "hash" };
        var service = new UserProfileService(new FakeUserProfileRepository(account));

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.CreateAsync(
            account.Id,
            new SaveUserProfileCommand("Display name", Gender.Unknown, null, null),
            CancellationToken.None));

        Assert.Equal(UserProfileError.AccountUnavailable, exception.Code);
    }

    [Fact]
    public async Task Update_changes_only_supplied_fields_and_uploads_supplied_cover()
    {
        var account = ActiveAccountWithUser();
        var repository = new FakeUserProfileRepository(account);
        var storage = new FakeProfileImageStorage();
        var service = new UserProfileService(repository, storage);
        await using var cover = new MemoryStream([1, 2, 3]);

        var result = await service.UpdateAsync(
            account.Id,
            new UpdateUserProfileCommand(
                null,
                Gender.Female,
                null,
                new ProfileImageFile(cover, "cover.jpg")),
            CancellationToken.None);

        Assert.Equal("Old name", result.DisplayName);
        Assert.Equal("https://cdn.viora.com/avatar-old.jpg", result.AvatarUrl);
        Assert.Equal("https://res.cloudinary.com/viora/cover.jpg", result.CoverUrl);
        Assert.Equal(Gender.Female, result.Gender);
        Assert.Single(storage.Uploads);
        Assert.Equal("cover", storage.Uploads[0].PublicId);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Update_rejects_when_profile_does_not_exist()
    {
        var account = ActiveAccount();
        var service = new UserProfileService(new FakeUserProfileRepository(account));

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.UpdateAsync(
            account.Id,
            new UpdateUserProfileCommand("New name", null, null, null),
            CancellationToken.None));

        Assert.Equal(UserProfileError.ProfileNotFound, exception.Code);
    }

    [Fact]
    public async Task Update_rejects_empty_patch()
    {
        var account = ActiveAccountWithUser();
        var service = new UserProfileService(new FakeUserProfileRepository(account));

        var exception = await Assert.ThrowsAsync<UserProfileException>(() => service.UpdateAsync(
            account.Id,
            new UpdateUserProfileCommand(null, null, null, null),
            CancellationToken.None));

        Assert.Equal(UserProfileError.InvalidProfile, exception.Code);
    }

    [Fact]
    public async Task Update_rejects_non_https_storage_url_without_saving()
    {
        var account = ActiveAccountWithUser();
        var repository = new FakeUserProfileRepository(account);
        var storage = new FakeProfileImageStorage("http://unsafe.example/avatar.jpg");
        var service = new UserProfileService(repository, storage);
        await using var avatar = new MemoryStream([1]);

        await Assert.ThrowsAsync<ProfileImageStorageException>(() => service.UpdateAsync(
            account.Id,
            new UpdateUserProfileCommand(null, null, new ProfileImageFile(avatar, "avatar.jpg"), null),
            CancellationToken.None));

        Assert.Equal(0, repository.SaveCount);
        Assert.Equal("https://cdn.viora.com/avatar-old.jpg", account.User!.AvatarUrl);
    }

    private static Account ActiveAccount() =>
        new() { Status = AccountStatus.Active, Role = AccountRole.User, Email = "user@example.com", PasswordHash = "hash" };

    private static Account ActiveAccountWithUser()
    {
        var account = ActiveAccount();
        account.User = new User
        {
            AccountId = account.Id,
            Account = account,
            DisplayName = "Old name",
            AvatarUrl = "https://cdn.viora.com/avatar-old.jpg"
        };
        return account;
    }

    private sealed class FakeUserProfileRepository(Account? account) : IUserProfileRepository
    {
        public int SaveCount { get; private set; }

        public Task<Account?> GetAccountWithUserAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(account?.Id == accountId ? account : null);

        public Task AddAsync(User user, CancellationToken cancellationToken)
        {
            if (account is not null) account.User = user;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProfileImageStorage(string? fixedUrl = null) : IProfileImageStorage
    {
        public List<ProfileImageUpload> Uploads { get; } = [];

        public Task<string> UploadAsync(ProfileImageUpload upload, CancellationToken cancellationToken)
        {
            Uploads.Add(upload);
            return Task.FromResult(fixedUrl ?? $"https://res.cloudinary.com/viora/{upload.PublicId}.jpg");
        }
    }
}
