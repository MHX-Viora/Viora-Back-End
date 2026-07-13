using Viora.Domain.Entities;

namespace Viora.Application.Users;

public sealed class UserProfileService(IUserProfileRepository repository) : IUserProfileService
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
        var user = new User
        {
            AccountId = account.Id,
            Account = account,
            DisplayName = command.DisplayName.Trim(),
            AvatarUrl = NormalizeUrl(command.AvatarUrl),
            CoverUrl = NormalizeUrl(command.CoverUrl),
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
        SaveUserProfileCommand command,
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
        account.User.DisplayName = command.DisplayName.Trim();
        account.User.AvatarUrl = NormalizeUrl(command.AvatarUrl);
        account.User.CoverUrl = NormalizeUrl(command.CoverUrl);
        account.User.Gender = command.Gender;

        await repository.SaveChangesAsync(cancellationToken);
        return Map(account, account.User);
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
        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            throw new UserProfileException(UserProfileError.InvalidProfile, "Tên hiển thị không được để trống.");
        }
        if (!Enum.IsDefined(command.Gender))
        {
            throw new UserProfileException(UserProfileError.InvalidProfile, "Giới tính không hợp lệ.");
        }
        if (!IsValidUrl(command.AvatarUrl) || !IsValidUrl(command.CoverUrl))
        {
            throw new UserProfileException(
                UserProfileError.InvalidProfile,
                "URL ảnh đại diện hoặc ảnh bìa không hợp lệ.");
        }
    }

    private static bool IsValidUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? NormalizeUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UserResponse Map(Account account, User user) => new(
        user.Id,
        account.Id,
        user.DisplayName,
        user.AvatarUrl,
        user.CoverUrl,
        account.Role,
        user.IsVerified,
        user.IdentityStatus);
}
