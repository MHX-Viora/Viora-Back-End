using Viora.Domain.Entities;
using Viora.Application.Users;

namespace Viora.Application.Accounts;

public sealed record RegisterAccountCommand(string Identifier, string Password);
public sealed record LoginAccountCommand(string Identifier, string Password);

public enum LoginOutcome { InvalidCredentials, Banned, Active, Deleted }

public sealed record AccountTokens(string AccessToken);

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
    Task<AccountResponse?> UpdateAsync(Guid id, UpdateAccountCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAccountRepository
{
    Task<(IReadOnlyList<Account> Items, int Total)> ListAsync(int skip, int take, CancellationToken cancellationToken);
    Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Account?> FindByIdentifierAsync(string? email, string? phone, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string email, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> PhoneExistsAsync(string phone, Guid? excludingId, CancellationToken cancellationToken);
    Task AddAsync(Account account, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    AccountTokens CreateTokens(Account account);
}

public sealed class AccountConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AccountValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
