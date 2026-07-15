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

    public async Task<IReadOnlyList<Hashtag>> GetHashtagsByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        return await dbContext.Hashtags
            .Where(hashtag => names.Contains(hashtag.Name))
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(Post post, CancellationToken cancellationToken) =>
        dbContext.Posts.AddAsync(post, cancellationToken).AsTask();

    public Task AddHashtagAsync(Hashtag hashtag, CancellationToken cancellationToken) =>
        dbContext.Hashtags.AddAsync(hashtag, cancellationToken).AsTask();

    public Task AddPostHashtagAsync(PostHashtag postHashtag, CancellationToken cancellationToken) =>
        dbContext.PostHashtags.AddAsync(postHashtag, cancellationToken).AsTask();
}
