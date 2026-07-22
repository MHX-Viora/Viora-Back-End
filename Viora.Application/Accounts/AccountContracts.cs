using FluentValidation;
using Viora.Domain.Entities;
using Viora.Application.Users;

namespace Viora.Application.Accounts;

public sealed record RegisterAccountCommand(string Identifier, string Password);
public sealed record LoginAccountCommand(string Identifier, string Password);
public sealed record RefreshAccountTokenCommand(string RefreshToken);
public sealed record LogoutAccountCommand(string? RefreshToken, Guid AccountId);
public sealed record ChangePasswordCommand(Guid AccountId, string CurrentPassword, string NewPassword, string ConfirmPassword);

public enum LoginOutcome { InvalidCredentials, Banned, Active, Deleted }

public sealed record AccountTokens(string AccessToken, string RefreshToken);
public sealed record IssuedAccountTokens(
    AccountTokens Tokens,
    string RefreshTokenHash,
    DateTime RefreshTokenExpiresAt);

public enum RefreshTokenOutcome { Active, Invalid }
public enum ChangePasswordOutcome { Success, AccountNotFound, InvalidCurrentPassword, SamePassword, ValidationFailed }

public sealed record RefreshAccountTokenResult(
    RefreshTokenOutcome Outcome,
    AccountTokens? Tokens,
    string? Message);

public sealed record ChangePasswordResult(
    ChangePasswordOutcome Outcome,
    string Message);

public sealed record LoginAccountResult(
    LoginOutcome Outcome,
    AccountStatus? Status,
    string? Message,
    AccountTokens? Tokens,
    UserResponse? User);

public sealed record UpdateAccountCommand(
    string? Email,
    string? Phone,
    string? Password);

public sealed record AccountResponse(
    Guid Id,
    string? Email,
    string? Phone,
    AccountRole Role,
    AccountStatus Status,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? DeletedAt);

public sealed record PaginationResponse(int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record PagedAccountResponse(IReadOnlyList<AccountResponse> Data, PaginationResponse Pagination);

public interface IAccountService
{
    Task<PagedAccountResponse> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<AccountResponse> RegisterAsync(RegisterAccountCommand command, CancellationToken cancellationToken);
    Task<LoginAccountResult> LoginAsync(LoginAccountCommand command, CancellationToken cancellationToken);
    Task<RefreshAccountTokenResult> RefreshTokenAsync(
        RefreshAccountTokenCommand command,
        CancellationToken cancellationToken);
    Task LogoutAsync(LogoutAccountCommand command, CancellationToken cancellationToken);
    Task<ChangePasswordResult> ChangePasswordAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken);
    Task<AccountResponse?> UpdateAsync(Guid id, UpdateAccountCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAccountRepository
{
    Task<(IReadOnlyList<Account> Items, int Total)> ListAsync(int skip, int take, CancellationToken cancellationToken);
    Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Account?> FindByIdentifierAsync(string? email, string? phone, CancellationToken cancellationToken);
    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string email, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> PhoneExistsAsync(string phone, Guid? excludingId, CancellationToken cancellationToken);
    Task AddAsync(Account account, CancellationToken cancellationToken);
    Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken);
    Task<bool> RotateRefreshTokenAsync(
        Guid currentTokenId,
        RefreshToken replacement,
        DateTime revokedAt,
        CancellationToken cancellationToken);
    Task RevokeRefreshTokenAsync(string tokenHash, Guid accountId, DateTime revokedAt, CancellationToken cancellationToken);
    Task RevokeRefreshTokensForAccountAsync(Guid accountId, DateTime revokedAt, CancellationToken cancellationToken);
    Task ChangePasswordAndRevokeRefreshTokensAsync(Account account, string passwordHash, DateTime changedAt, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    IssuedAccountTokens CreateTokens(Account account);
    string HashRefreshToken(string refreshToken);
}

public sealed class AccountConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AccountValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Mật khẩu hiện tại không được để trống.");
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
            .Length(8, 100).WithMessage("Mật khẩu mới phải từ 8-100 ký tự.")
            .Matches("[A-Z]").WithMessage("Mật khẩu mới phải chứa ít nhất 1 chữ hoa.")
            .Matches("[a-z]").WithMessage("Mật khẩu mới phải chứa ít nhất 1 chữ thường.")
            .Matches("[0-9]").WithMessage("Mật khẩu mới phải chứa ít nhất 1 số.");
        RuleFor(x => x.ConfirmPassword).NotEmpty().WithMessage("Xác nhận mật khẩu không được để trống.");
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword)
            .When(x => !string.IsNullOrWhiteSpace(x.ConfirmPassword) && !string.IsNullOrWhiteSpace(x.NewPassword))
            .WithMessage("Xác nhận mật khẩu không khớp.");
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .When(x => !string.IsNullOrWhiteSpace(x.CurrentPassword) && !string.IsNullOrWhiteSpace(x.NewPassword))
            .WithMessage("Mật khẩu mới không được trùng mật khẩu cũ.");
    }
}
