using Viora.Domain.Entities;

namespace Viora.Application.Users;

public sealed record ProfileImageFile(Stream Content, string FileName);

public sealed record SaveUserProfileCommand(
    string DisplayName,
    Gender Gender,
    ProfileImageFile? Avatar,
    ProfileImageFile? Cover);

public sealed record UpdateUserProfileCommand(
    string? DisplayName,
    Gender? Gender,
    ProfileImageFile? Avatar,
    ProfileImageFile? Cover);

public sealed record ProfileImageUpload(
    Stream Content,
    string FileName,
    string Folder,
    string PublicId);

public sealed record UserResponse(
    Guid Id,
    Guid AccountId,
    string DisplayName,
    string? AvatarUrl,
    string? CoverUrl,
    Gender Gender,
    AccountRole Role,
    bool IsVerified,
    UserIdentityState VerificationStatus);

public enum UserProfileError
{
    AccountUnavailable,
    ProfileAlreadyExists,
    ProfileNotFound,
    InvalidProfile
}

public sealed class UserProfileException(UserProfileError code, string message) : Exception(message)
{
    public UserProfileError Code { get; } = code;
}

public interface IUserProfileService
{
    Task<UserResponse> CreateAsync(Guid accountId, SaveUserProfileCommand command, CancellationToken cancellationToken);
    Task<UserResponse> UpdateAsync(Guid accountId, UpdateUserProfileCommand command, CancellationToken cancellationToken);
}

public interface IProfileImageStorage
{
    Task<string> UploadAsync(ProfileImageUpload upload, CancellationToken cancellationToken);
}

public sealed class ProfileImageStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IUserProfileRepository
{
    Task<Account?> GetAccountWithUserAsync(Guid accountId, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
