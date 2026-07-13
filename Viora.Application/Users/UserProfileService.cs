using Viora.Domain.Entities;

namespace Viora.Application.Users;

public sealed class UserProfileService(
    IUserProfileRepository repository,
    IProfileImageStorage? imageStorage = null) : IUserProfileService
{
    public async Task<UserResponse> CreateAsync(
        Guid accountId,
        SaveUserProfileCommand command,
        CancellationToken cancellationToken)
    {
        var account = await GetActiveAccountAsync(accountId, cancellationToken);
        if (account.User is not null)
        {
            throw new UserProfileException(
                UserProfileError.ProfileAlreadyExists,
                "Hồ sơ người dùng đã tồn tại.");
        }

        Validate(command);
        var avatarUrl = await UploadImageAsync(account.Id, command.Avatar, "avatar", cancellationToken);
        var coverUrl = await UploadImageAsync(account.Id, command.Cover, "cover", cancellationToken);
        var user = new User
        {
            AccountId = account.Id,
            Account = account,
            DisplayName = command.DisplayName.Trim(),
            AvatarUrl = avatarUrl,
            CoverUrl = coverUrl,
            Gender = command.Gender,
            IsVerified = false,
            IdentityStatus = UserIdentityState.NotVerified
        };
        account.User = user;

        await repository.AddAsync(user, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(account, user);
    }

    public async Task<UserResponse> UpdateAsync(
        Guid accountId,
        UpdateUserProfileCommand command,
        CancellationToken cancellationToken)
    {
        var account = await GetActiveAccountAsync(accountId, cancellationToken);
        if (account.User is null)
        {
            throw new UserProfileException(
                UserProfileError.ProfileNotFound,
                "Không tìm thấy hồ sơ người dùng.");
        }

        Validate(command);
        var avatarUrl = await UploadImageAsync(account.Id, command.Avatar, "avatar", cancellationToken);
        var coverUrl = await UploadImageAsync(account.Id, command.Cover, "cover", cancellationToken);

        if (command.DisplayName is not null)
        {
            account.User.DisplayName = command.DisplayName.Trim();
        }
        if (command.Gender.HasValue)
        {
            account.User.Gender = command.Gender.Value;
        }
        if (avatarUrl is not null)
        {
            account.User.AvatarUrl = avatarUrl;
        }
        if (coverUrl is not null)
        {
            account.User.CoverUrl = coverUrl;
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Map(account, account.User);
    }

    private async Task<string?> UploadImageAsync(
        Guid accountId,
        ProfileImageFile? image,
        string publicId,
        CancellationToken cancellationToken)
    {
        if (image is null)
        {
            return null;
        }

        var storage = imageStorage ?? throw new ProfileImageStorageException(
            "Dịch vụ lưu trữ ảnh chưa được cấu hình.");
        var url = await storage.UploadAsync(
            new ProfileImageUpload(
                image.Content,
                image.FileName,
                $"viora/users/{accountId:N}/profile",
                publicId),
            cancellationToken);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ProfileImageStorageException("Dịch vụ lưu trữ ảnh trả về URL không hợp lệ.");
        }
        return url;
    }

    private async Task<Account> GetActiveAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await repository.GetAccountWithUserAsync(accountId, cancellationToken);
        if (account is null || account.Status != AccountStatus.Active)
        {
            throw new UserProfileException(
                UserProfileError.AccountUnavailable,
                "Tài khoản không tồn tại hoặc không ở trạng thái hoạt động.");
        }
        return account;
    }

    private static void Validate(SaveUserProfileCommand command)
    {
        ValidateDisplayName(command.DisplayName);
        if (!Enum.IsDefined(command.Gender))
        {
            throw InvalidProfile("Giới tính không hợp lệ.");
        }
    }

    private static void Validate(UpdateUserProfileCommand command)
    {
        if (command.DisplayName is null && command.Gender is null &&
            command.Avatar is null && command.Cover is null)
        {
            throw InvalidProfile("Yêu cầu cập nhật không có dữ liệu.");
        }
        if (command.DisplayName is not null)
        {
            ValidateDisplayName(command.DisplayName);
        }
        if (command.Gender.HasValue && !Enum.IsDefined(command.Gender.Value))
        {
            throw InvalidProfile("Giới tính không hợp lệ.");
        }
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
        {
            throw InvalidProfile("Tên hiển thị phải có từ 1 đến 100 ký tự.");
        }
    }

    private static UserProfileException InvalidProfile(string message) =>
        new(UserProfileError.InvalidProfile, message);

    private static UserResponse Map(Account account, User user) => new(
        user.Id,
        account.Id,
        user.DisplayName,
        user.AvatarUrl,
        user.CoverUrl,
        user.Gender,
        account.Role,
        user.IsVerified,
        user.IdentityStatus);
}
