using Microsoft.EntityFrameworkCore;
using Viora.Application.Hashtags;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class HashtagRepository(AppDbContext dbContext) : IHashtagRepository
{
    public Task<Hashtag?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Hashtags.SingleOrDefaultAsync(
            hashtag => hashtag.Name == name,
            cancellationToken);

    public Task AddAsync(Hashtag hashtag, CancellationToken cancellationToken) =>
        dbContext.Hashtags.AddAsync(hashtag, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<HashtagSearchResponse> SearchAsync(
        SearchHashtagsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var hashtags = dbContext.Hashtags.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = $"%{query.Keyword}%";
            hashtags = hashtags.Where(hashtag => EF.Functions.ILike(hashtag.Name, keyword));
        }

        var totalItems = await hashtags.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await hashtags
            .OrderByDescending(hashtag => hashtag.PostCount)
            .ThenBy(hashtag => hashtag.Name)
            .Skip(skip)
            .Take(pageSize)
            .Select(hashtag => new HashtagSearchItemResponse(
                hashtag.Id,
                hashtag.Name,
                hashtag.PostCount))
            .ToListAsync(cancellationToken);

        return new HashtagSearchResponse(page, pageSize, totalItems, totalPages, items);
    }
}
