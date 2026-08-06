using Viora.Application.Users;
using Viora.Domain.Entities;

namespace Viora.Application.Accounts;

public sealed class GoogleLoginService(
    IGoogleIdentityTokenVerifier identityTokenVerifier,
    IGoogleLoginRepository repository,
    ITokenService tokenService) : IGoogleLoginService
{
    public async Task<LoginAccountResult> LoginAsync(
        GoogleLoginCommand command,
        CancellationToken cancellationToken)
    {
        var identity = await identityTokenVerifier.VerifyAsync(
            command.FirebaseToken,
            cancellationToken);
        if (identity is null)
        {
            return InvalidCredentials();
        }

        var account = await repository.ResolveAccountAsync(identity, cancellationToken);
        if (account.Status == AccountStatus.Banned)
        {
            return new LoginAccountResult(
                LoginOutcome.Banned,
                account.Status,
                "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ hỗ trợ.",
                null,
                null);
        }

        if (account.Status == AccountStatus.Deleted || account.DeletedAt is not null)
        {
            return new LoginAccountResult(
                LoginOutcome.Deleted,
                AccountStatus.Deleted,
                "Tài khoản này không còn tồn tại hoặc đã bị xóa.",
                null,
                null);
        }

        if (account.Status != AccountStatus.Active)
        {
            return InvalidCredentials();
        }

        var issued = tokenService.CreateTokens(account);
        var loginAt = DateTime.UtcNow;
        await repository.CompleteLoginAsync(
            account,
            new RefreshToken
            {
                AccountId = account.Id,
                TokenHash = issued.RefreshTokenHash,
                ExpiresAt = issued.RefreshTokenExpiresAt
            },
            loginAt,
            cancellationToken);

        return new LoginAccountResult(
            LoginOutcome.Active,
            account.Status,
            null,
            issued.Tokens,
            MapUser(account));
    }

    private static LoginAccountResult InvalidCredentials() => new(
        LoginOutcome.InvalidCredentials,
        null,
        "Không thể xác thực tài khoản Google.",
        null,
        null);

    private static UserResponse? MapUser(Account account) => account.User is null
        ? null
        : new UserResponse(
            account.User.Id,
            account.Id,
            account.User.DisplayName,
            account.User.AvatarUrl,
            account.User.CoverUrl,
            account.User.Gender,
            account.Role,
            account.User.IsVerified,
            account.User.IdentityStatus);
}
