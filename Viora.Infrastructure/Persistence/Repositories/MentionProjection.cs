using Microsoft.EntityFrameworkCore;
using Viora.Application.Mentions;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

internal static class MentionProjection
{
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MentionResponse>>> LoadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> targetIds,
        IReadOnlyCollection<MentionTargetType> targetTypes,
        CancellationToken cancellationToken)
    {
        if (targetIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<MentionResponse>>();

        var rows = await db.Mentions
            .AsNoTracking()
            .Where(mention => targetIds.Contains(mention.TargetId) && targetTypes.Contains(mention.TargetType))
            .OrderBy(mention => mention.CreatedAt)
            .Select(mention => new
            {
                mention.TargetId,
                Value = new MentionResponse(mention.MentionedUserId, mention.MentionedUser.DisplayName)
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.TargetId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MentionResponse>)group.Select(row => row.Value).ToArray());
    }
}
