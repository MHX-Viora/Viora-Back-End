using Microsoft.EntityFrameworkCore;
using Viora.Application.Mentions;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class MentionRepository(AppDbContext db) : IMentionRepository
{
    private const string Vietnamese = "áàảãạăắằẳẵặâấầẩẫậđéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵ";
    private const string Latin = "aaaaaaaaaaaaaaaaadeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyy";

    public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Include(user => user.Account).SingleOrDefaultAsync(user =>
            user.Id == userId &&
            user.Account.Status == AccountStatus.Active &&
            user.Account.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<User>> GetEligibleUsersAsync(
        Guid mentionedByUserId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken) =>
        await db.Users
            .Include(user => user.Account)
            .Where(user =>
                userIds.Contains(user.Id) &&
                user.Id != mentionedByUserId &&
                user.Account.Status == AccountStatus.Active &&
                user.Account.DeletedAt == null &&
                (user.Settings == null || user.Settings.AllowMention) &&
                !db.Friendships.Any(friendship =>
                    friendship.Status == FriendshipStatus.Blocked &&
                    ((friendship.RequesterUserId == mentionedByUserId && friendship.AddresseeUserId == user.Id) ||
                     (friendship.RequesterUserId == user.Id && friendship.AddresseeUserId == mentionedByUserId))))
            .ToListAsync(cancellationToken);

    public Task AddRangeAsync(IReadOnlyList<Mention> mentions, CancellationToken cancellationToken) =>
        db.Mentions.AddRangeAsync(mentions, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<MentionSearchResponse>> SearchAsync(
        Guid currentUserId,
        string keyword,
        CancellationToken cancellationToken)
    {
        var normalized = RemoveVietnameseMarks(keyword.ToLowerInvariant());
        return await db.Users
            .AsNoTracking()
            .Where(user =>
                user.Id != currentUserId &&
                user.Account.Status == AccountStatus.Active &&
                user.Account.DeletedAt == null &&
                (user.Settings == null || user.Settings.AllowMention) &&
                EF.Functions.ILike(
                    AppDbContext.Translate(user.DisplayName.ToLower(), Vietnamese, Latin),
                    $"%{normalized}%") &&
                !db.Friendships.Any(friendship =>
                    friendship.Status == FriendshipStatus.Blocked &&
                    ((friendship.RequesterUserId == currentUserId && friendship.AddresseeUserId == user.Id) ||
                     (friendship.RequesterUserId == user.Id && friendship.AddresseeUserId == currentUserId))))
            .OrderBy(user => db.Friendships.Any(friendship =>
                friendship.Status == FriendshipStatus.Accepted &&
                ((friendship.RequesterUserId == currentUserId && friendship.AddresseeUserId == user.Id) ||
                 (friendship.RequesterUserId == user.Id && friendship.AddresseeUserId == currentUserId))) ? 0 :
                db.Follows.Any(follow => follow.FollowerId == currentUserId && follow.FollowingId == user.Id) ? 1 :
                db.Follows.Any(follow => follow.FollowerId == user.Id && follow.FollowingId == currentUserId) ? 2 : 3)
            .ThenBy(user => user.DisplayName)
            .Take(20)
            .Select(user => new MentionSearchResponse(user.Id, user.DisplayName, user.AvatarUrl, user.IsVerified))
            .ToListAsync(cancellationToken);
    }

    private static string RemoveVietnameseMarks(string value)
    {
        var result = value;
        for (var index = 0; index < Vietnamese.Length; index++)
        {
            result = result.Replace(Vietnamese[index], Latin[index]);
        }
        return result;
    }
}
