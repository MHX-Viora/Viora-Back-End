using Viora.Application.Posts;

namespace Viora.Infrastructure.Persistence;

public sealed class EfUnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
