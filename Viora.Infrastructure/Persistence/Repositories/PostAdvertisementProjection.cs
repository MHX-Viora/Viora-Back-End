using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

internal static class PostAdvertisementProjection
{
    public static async Task<Dictionary<Guid, PostAdvertisementResponse>> LoadAsync(
        AppDbContext dbContext, IReadOnlyCollection<Guid> postIds, Guid? viewerId,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0) return [];
        var now = DateTime.UtcNow;
        var campaigns = await dbContext.Advertisements.AsNoTracking()
            .Where(ad => postIds.Contains(ad.PostId) && ad.Status == AdvertisementStatus.Active &&
                ad.StartAt <= now && ad.EndAt > now && ad.Post.Status == PostStatus.Published &&
                ad.Post.DeletedAt == null && ad.Post.Visibility == PostVisibility.Public &&
                ad.Post.User.Account.Status == AccountStatus.Active)
            .OrderByDescending(ad => ad.UpdatedAt).ThenByDescending(ad => ad.Id)
            .Select(ad => new
            {
                ad.PostId, ad.ReservedAmount, ad.TotalBudget, ad.SpentAmount,
                Presentation = new PostAdvertisementResponse(ad.Id, ad.CtaType, ad.DestinationUrl,
                    ad.Headline, ad.Description, ad.AdvertiserId == viewerId)
            })
            .ToListAsync(cancellationToken);

        // Evaluate decimal eligibility after the batch query to support SQLite as well as PostgreSQL.
        return campaigns.Where(ad => ad.ReservedAmount > 0 && ad.SpentAmount < ad.TotalBudget)
            .GroupBy(ad => ad.PostId)
            .ToDictionary(group => group.Key, group => group.First().Presentation);
    }
}
