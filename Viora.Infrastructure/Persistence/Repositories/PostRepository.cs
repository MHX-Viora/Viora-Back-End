using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class PostRepository(AppDbContext dbContext) : IPostRepository
{
    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .Include(user => user.Account)
            .SingleOrDefaultAsync(user =>
                user.Id == userId &&
                user.Account.Status == AccountStatus.Active &&
                user.Account.DeletedAt == null,
                cancellationToken);

    public Task AddAsync(Post post, CancellationToken cancellationToken) =>
        dbContext.Posts.AddAsync(post, cancellationToken).AsTask();
}
