using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class ForgotPasswordRepository(AppDbContext dbContext) : IForgotPasswordRepository
{
    public Task<Account?> FindByIdentifierAsync(
        string? email,
        IReadOnlyList<string> phoneCandidates,
        CancellationToken cancellationToken) =>
        ActiveAccounts()
            .FirstOrDefaultAsync(
                account =>
                    (email != null && account.Email == email) ||
                    (account.Phone != null && phoneCandidates.Contains(account.Phone)),
                cancellationToken);

    public Task<Account?> FindByPhoneAsync(
        IReadOnlyList<string> phoneCandidates,
        CancellationToken cancellationToken) =>
        ActiveAccounts()
            .FirstOrDefaultAsync(
                account => account.Phone != null && phoneCandidates.Contains(account.Phone),
                cancellationToken);

    public Task<Account?> GetAsync(Guid accountId, CancellationToken cancellationToken) =>
        ActiveAccounts().FirstOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    public Task<bool> PhoneExistsAsync(
        string phoneNumber,
        Guid excludingAccountId,
        CancellationToken cancellationToken)
    {
        var candidates = ForgotPasswordIdentifier.PhoneCandidates(phoneNumber);
        return dbContext.Accounts.AnyAsync(
            account =>
                account.Id != excludingAccountId &&
                account.DeletedAt == null &&
                account.Phone != null &&
                candidates.Contains(account.Phone),
            cancellationToken);
    }

    public async Task SavePhoneAsync(
        Account account,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        account.Phone = phoneNumber;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePasswordAndRevokeRefreshTokensAsync(
        Account account,
        string passwordHash,
        DateTime changedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        account.PasswordHash = passwordHash;
        await dbContext.RefreshTokens
            .Where(token =>
                token.AccountId == account.Id &&
                token.RevokedAt == null &&
                token.ExpiresAt > changedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, changedAt),
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<Account> ActiveAccounts() =>
        dbContext.Accounts.Where(account =>
            account.DeletedAt == null &&
            account.Status == AccountStatus.Active);
}

