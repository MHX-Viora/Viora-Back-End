using Viora.Domain.Entities;

namespace Viora.Application.Users;

public sealed record SaveUserProfileCommand(
    string DisplayName,
    string? AvatarUrl,
    string? CoverUrl,
    Gender Gender);

public sealed record UserResponse(
    Guid Id,
    Guid AccountId,
    string DisplayName,
    string? AvatarUrl,
    string? CoverUrl,
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
    Task<UserResponse> UpdateAsync(Guid accountId, SaveUserProfileCommand command, CancellationToken cancellationToken);
}

public interface IUserProfileRepository
{
    Task<Account?> GetAccountWithUserAsync(Guid accountId, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
