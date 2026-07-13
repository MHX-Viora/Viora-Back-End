using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Application.Users;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class UserProfileRepository(AppDbContext dbContext) : IUserProfileRepository
{
    public Task<Account?> GetAccountWithUserAsync(Guid accountId, CancellationToken cancellationToken) =>
        dbContext.Accounts
            .Include(account => account.User)
            .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    public Task AddAsync(User user, CancellationToken cancellationToken) =>
        dbContext.Users.AddAsync(user, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UserProfileException(
                UserProfileError.ProfileAlreadyExists,
                "Hồ sơ người dùng đã tồn tại.");
        }
    }
}
