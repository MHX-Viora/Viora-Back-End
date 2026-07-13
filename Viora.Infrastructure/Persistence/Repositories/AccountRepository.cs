using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Application.Accounts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(AppDbContext dbContext) : IAccountRepository
{
    public async Task<(IReadOnlyList<Account> Items, int Total)> ListAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accounts.AsNoTracking().Where(account => account.DeletedAt == null);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(account => account.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accounts.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public Task<Account?> FindByIdentifierAsync(
        string? email,
        string? phone,
        CancellationToken cancellationToken) =>
        dbContext.Accounts
            .Include(account => account.User)
            .SingleOrDefaultAsync(
                account => email != null ? account.Email == email : account.Phone == phone,
                cancellationToken);

    public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Include(token => token.Account)
            .ThenInclude(account => account.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public Task<bool> EmailExistsAsync(
        string email,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        dbContext.Accounts.AnyAsync(
            account => (!excludingId.HasValue || account.Id != excludingId.Value) && account.Email == email,
            cancellationToken);

    public Task<bool> PhoneExistsAsync(
        string phone,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        dbContext.Accounts.AnyAsync(
            account => (!excludingId.HasValue || account.Id != excludingId.Value) && account.Phone == phone,
            cancellationToken);

    public Task AddAsync(Account account, CancellationToken cancellationToken) =>
        dbContext.Accounts.AddAsync(account, cancellationToken).AsTask();

    public Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken).AsTask();

    public async Task<bool> RotateRefreshTokenAsync(
        Guid currentTokenId,
        RefreshToken replacement,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var changed = await dbContext.RefreshTokens
            .Where(token => token.Id == currentTokenId && token.RevokedAt == null && token.ExpiresAt > revokedAt)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, revokedAt)
                    .SetProperty(token => token.ReplacedByTokenHash, replacement.TokenHash)
                    .SetProperty(token => token.UpdatedAt, revokedAt),
                cancellationToken);
        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await dbContext.RefreshTokens.AddAsync(replacement, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AccountConflictException("ACCOUNT_CONFLICT", "Email or phone is already used by another account.");
        }
    }
}
