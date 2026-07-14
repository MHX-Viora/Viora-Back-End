using Viora.Domain.Entities;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Viora.Application.Users;

namespace Viora.Application.Accounts;

public sealed class AccountService(
    IAccountRepository repository,
    IPasswordHasher passwordHasher,
    ITokenService? tokenService = null) : IAccountService
{
    public async Task<PagedAccountResponse> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await repository.ListAsync((page - 1) * pageSize, pageSize, cancellationToken);
        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

        return new PagedAccountResponse(
            items.Select(Map).ToList(),
            new PaginationResponse(page, pageSize, total, totalPages));
    }

    public async Task<AccountResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await repository.GetAsync(id, cancellationToken);
        return account is null || account.DeletedAt is not null ? null : Map(account);
    }

    public async Task<AccountResponse> RegisterAsync(RegisterAccountCommand command, CancellationToken cancellationToken)
    {
        var (email, phone) = ParseIdentifier(command.Identifier);
        await EnsureUniqueAsync(email, phone, null, cancellationToken);

        var account = new Account
        {
            Email = email,
            Phone = phone,
            PasswordHash = passwordHasher.Hash(command.Password),
            Role = AccountRole.User,
            Status = AccountStatus.Active
        };

        await repository.AddAsync(account, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(account);
    }

    public async Task<LoginAccountResult> LoginAsync(
        LoginAccountCommand command,
        CancellationToken cancellationToken)
    {
        (string? Email, string? Phone) identifier;
        try
        {
            identifier = ParseIdentifier(command.Identifier);
        }
        catch (AccountValidationException)
        {
            return InvalidCredentials();
        }

        var account = await repository.FindByIdentifierAsync(
            identifier.Email,
            identifier.Phone,
            cancellationToken);

        if (account is null || !passwordHasher.Verify(command.Password, account.PasswordHash))
        {
            return InvalidCredentials();
        }

        if (account.Status == AccountStatus.Banned)
        {
            return new LoginAccountResult(
                LoginOutcome.Banned,
                account.Status,
                "Tài khoản của bạn đã bị khóa do vi phạm Tiêu chuẩn cộng đồng. Vui lòng liên hệ hỗ trợ nếu cho rằng có sự nhầm lẫn.",
                null,
                null);
        }

        if (account.Status == AccountStatus.Deleted)
        {
            return new LoginAccountResult(
                LoginOutcome.Deleted,
                account.Status,
                "Tài khoản này không còn tồn tại hoặc đã bị xóa.",
                null,
                null);
        }

        if (account.Status != AccountStatus.Active)
        {
            return InvalidCredentials();
        }

        var issuedTokens = (tokenService ?? throw new InvalidOperationException("Token service is not configured."))
            .CreateTokens(account);
        await repository.AddRefreshTokenAsync(new RefreshToken
        {
            AccountId = account.Id,
            TokenHash = issuedTokens.RefreshTokenHash,
            ExpiresAt = issuedTokens.RefreshTokenExpiresAt
        }, cancellationToken);
        account.LastLoginAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);

        return new LoginAccountResult(
            LoginOutcome.Active,
            account.Status,
            null,
            issuedTokens.Tokens,
            MapLoginUser(account));
    }

    public async Task<RefreshAccountTokenResult> RefreshTokenAsync(
        RefreshAccountTokenCommand command,
        CancellationToken cancellationToken)
    {
        var tokens = tokenService ?? throw new InvalidOperationException("Token service is not configured.");
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return InvalidRefreshToken();
        }

        var current = await repository.FindRefreshTokenAsync(
            tokens.HashRefreshToken(command.RefreshToken),
            cancellationToken);
        if (current is null || current.RevokedAt is not null || current.ExpiresAt <= DateTime.UtcNow ||
            current.Account.Status != AccountStatus.Active || current.Account.DeletedAt is not null)
        {
            return InvalidRefreshToken();
        }

        var issued = tokens.CreateTokens(current.Account);
        var replacement = new RefreshToken
        {
            AccountId = current.AccountId,
            TokenHash = issued.RefreshTokenHash,
            ExpiresAt = issued.RefreshTokenExpiresAt
        };
        var rotated = await repository.RotateRefreshTokenAsync(
            current.Id,
            replacement,
            DateTime.UtcNow,
            cancellationToken);

        return rotated
            ? new RefreshAccountTokenResult(RefreshTokenOutcome.Active, issued.Tokens, null)
            : InvalidRefreshToken();
    }

    public async Task LogoutAsync(LogoutAccountCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            await repository.RevokeRefreshTokensForAccountAsync(
                command.AccountId,
                DateTime.UtcNow,
                cancellationToken);
            return;
        }

        var tokens = tokenService ?? throw new InvalidOperationException("Token service is not configured.");
        await repository.RevokeRefreshTokenAsync(
            tokens.HashRefreshToken(command.RefreshToken),
            command.AccountId,
            DateTime.UtcNow,
            cancellationToken);
    }

    public async Task<AccountResponse?> UpdateAsync(
        Guid id,
        UpdateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetAsync(id, cancellationToken);
        if (account is null || account.DeletedAt is not null)
        {
            return null;
        }

        var email = NormalizeEmail(command.Email);
        var phone = NormalizePhone(command.Phone);
        await EnsureUniqueAsync(email, phone, id, cancellationToken);

        account.Email = email;
        account.Phone = phone;
        if (!string.IsNullOrWhiteSpace(command.Password))
        {
            account.PasswordHash = passwordHasher.Hash(command.Password);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Map(account);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await repository.GetAsync(id, cancellationToken);
        if (account is null || account.DeletedAt is not null)
        {
            return;
        }

        account.Status = AccountStatus.Deleted;
        account.DeletedAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUniqueAsync(string? email, string? phone, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (email is not null && await repository.EmailExistsAsync(email, excludingId, cancellationToken))
        {
            throw new AccountConflictException("EMAIL_EXISTS", "An account with this email already exists.");
        }

        if (phone is not null && await repository.PhoneExistsAsync(phone, excludingId, cancellationToken))
        {
            throw new AccountConflictException("PHONE_EXISTS", "An account with this phone number already exists.");
        }
    }

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    private static string? NormalizePhone(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private static (string? Email, string? Phone) ParseIdentifier(string identifier)
    {
        var value = identifier.Trim();
        if (MailAddress.TryCreate(value, out var address) &&
            string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
        {
            return (value.ToLowerInvariant(), null);
        }

        var phone = Regex.Replace(value, "[\\s()\\-]", string.Empty);
        if (Regex.IsMatch(phone, "^\\+?[0-9]{8,15}$"))
        {
            return (null, phone);
        }

        throw new AccountValidationException(
            "INVALID_IDENTIFIER",
            "Identifier must be a valid email address or phone number.");
    }

    private static AccountResponse Map(Account account) => new(
        account.Id,
        account.Email,
        account.Phone,
        account.Role,
        account.Status,
        account.LastLoginAt,
        account.CreatedAt,
        account.UpdatedAt,
        account.DeletedAt);

    private static LoginAccountResult InvalidCredentials() => new(
        LoginOutcome.InvalidCredentials,
        null,
        "Thông tin đăng nhập hoặc mật khẩu không chính xác.",
        null,
        null);

    private static RefreshAccountTokenResult InvalidRefreshToken() => new(
        RefreshTokenOutcome.Invalid,
        null,
        "Refresh token không hợp lệ hoặc đã hết hạn.");

    private static UserResponse? MapLoginUser(Account account) => account.User is null
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
