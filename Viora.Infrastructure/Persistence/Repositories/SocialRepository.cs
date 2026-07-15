using Microsoft.EntityFrameworkCore;
using Viora.Application.Social;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class SocialRepository(AppDbContext dbContext) : ISocialRepository
{
    public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .Include(user => user.Account)
            .SingleOrDefaultAsync(user =>
                user.Id == userId &&
                user.Account.Status == AccountStatus.Active &&
                user.Account.DeletedAt == null,
                cancellationToken);

    public Task<Follow?> GetFollowAsync(Guid followerId, Guid followingId, CancellationToken cancellationToken) =>
        dbContext.Follows.SingleOrDefaultAsync(
            follow => follow.FollowerId == followerId && follow.FollowingId == followingId,
            cancellationToken);

    public Task<int> CountFollowersAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Follows.CountAsync(follow => follow.FollowingId == userId, cancellationToken);

    public Task<Friendship?> GetFriendshipBetweenAsync(
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken cancellationToken) =>
        dbContext.Friendships.SingleOrDefaultAsync(friendship =>
            (friendship.RequesterUserId == firstUserId && friendship.AddresseeUserId == secondUserId) ||
            (friendship.RequesterUserId == secondUserId && friendship.AddresseeUserId == firstUserId),
            cancellationToken);

    public Task<Friendship?> GetFriendshipAsync(Guid friendshipId, CancellationToken cancellationToken) =>
        dbContext.Friendships.SingleOrDefaultAsync(
            friendship => friendship.Id == friendshipId,
            cancellationToken);

    public Task<Guid?> GetPrivateConversationIdAsync(
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken cancellationToken) =>
        dbContext.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.ConversationType == ConversationType.Private &&
                conversation.Members.Any(member => member.UserId == firstUserId) &&
                conversation.Members.Any(member => member.UserId == secondUserId))
            .OrderByDescending(conversation => conversation.CreatedAt)
            .Select(conversation => (Guid?)conversation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<FriendRequestListResponse> GetPendingRequestsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.Friendships
            .AsNoTracking()
            .Where(friendship =>
                friendship.AddresseeUserId == userId &&
                friendship.Status == FriendshipStatus.Pending)
            .OrderByDescending(friendship => friendship.CreatedAt)
            .ThenBy(friendship => friendship.Id)
            .Select(friendship => new FriendRequestItemResponse(
                friendship.Id,
                friendship.RequesterUser.Id,
                friendship.RequesterUser.DisplayName,
                friendship.RequesterUser.AvatarUrl,
                friendship.RequesterUser.IsVerified,
                friendship.CreatedAt))
            .ToListAsync(cancellationToken);

        return new FriendRequestListResponse(items);
    }

    public async Task<FriendshipListResponse> GetFriendshipsAsync(
        GetFriendshipsQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var friendships = dbContext.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.Status == query.Status);

        friendships = query.Status switch
        {
            FriendshipStatus.Pending => friendships.Where(friendship => friendship.AddresseeUserId == query.CurrentUserId),
            FriendshipStatus.Rejected => friendships.Where(friendship => friendship.AddresseeUserId == query.CurrentUserId),
            FriendshipStatus.Accepted => friendships.Where(friendship =>
                friendship.RequesterUserId == query.CurrentUserId ||
                friendship.AddresseeUserId == query.CurrentUserId),
            _ => friendships.Where(_ => false)
        };

        var projected = friendships.Select(friendship => new
        {
            Friendship = friendship,
            OtherUserId = friendship.RequesterUserId == query.CurrentUserId
                ? friendship.AddresseeUserId
                : friendship.RequesterUserId,
            OtherUser = friendship.RequesterUserId == query.CurrentUserId
                ? friendship.AddresseeUser
                : friendship.RequesterUser
        })
        .Where(item =>
            item.OtherUser.Account.Status == AccountStatus.Active &&
            item.OtherUser.Account.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = $"%{query.Keyword.Trim()}%";
            projected = projected.Where(item => EF.Functions.ILike(item.OtherUser.DisplayName, keyword));
        }

        var totalItems = await projected.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await projected
            .OrderByDescending(item => item.Friendship.CreatedAt)
            .ThenByDescending(item => item.Friendship.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(item => new FriendshipListItemResponse(
                item.Friendship.Id,
                item.Friendship.Status.ToString(),
                item.Friendship.CreatedAt,
                item.Friendship.RespondedAt,
                new FriendshipUserResponse(
                    item.OtherUser.Id,
                    item.OtherUser.DisplayName,
                    item.OtherUser.AvatarUrl,
                    item.OtherUser.IsVerified,
                    dbContext.Friendships.Count(otherFriend =>
                        otherFriend.Status == FriendshipStatus.Accepted &&
                        (otherFriend.RequesterUserId == item.OtherUserId ||
                            otherFriend.AddresseeUserId == item.OtherUserId) &&
                        dbContext.Friendships.Any(myFriend =>
                            myFriend.Status == FriendshipStatus.Accepted &&
                            (myFriend.RequesterUserId == query.CurrentUserId ||
                                myFriend.AddresseeUserId == query.CurrentUserId) &&
                            (
                                myFriend.RequesterUserId == (otherFriend.RequesterUserId == item.OtherUserId
                                    ? otherFriend.AddresseeUserId
                                    : otherFriend.RequesterUserId) ||
                                myFriend.AddresseeUserId == (otherFriend.RequesterUserId == item.OtherUserId
                                    ? otherFriend.AddresseeUserId
                                    : otherFriend.RequesterUserId)
                            ))))))
            .ToListAsync(cancellationToken);

        return new FriendshipListResponse(page, pageSize, totalItems, totalPages, items);
    }

    public Task<UserStatisticsResponse?> GetStatisticsAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserStatisticsResponse(
                dbContext.Posts.Count(post =>
                    post.UserId == user.Id &&
                    post.Status == PostStatus.Published),
                dbContext.Follows.Count(follow => follow.FollowingId == user.Id),
                dbContext.Follows.Count(follow => follow.FollowerId == user.Id),
                dbContext.Friendships.Count(friendship =>
                    friendship.Status == FriendshipStatus.Accepted &&
                    (friendship.RequesterUserId == user.Id || friendship.AddresseeUserId == user.Id))))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<UserProfileSummaryResponse?> GetUserProfileSummaryAsync(
        Guid currentUserId,
        Guid targetUserId,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == targetUserId)
            .Select(user => new
            {
                User = user,
                PostCount = dbContext.Posts.Count(post =>
                    post.UserId == user.Id &&
                    post.Status == PostStatus.Published),
                FollowerCount = dbContext.Follows.Count(follow => follow.FollowingId == user.Id),
                FollowingCount = dbContext.Follows.Count(follow => follow.FollowerId == user.Id),
                FriendCount = dbContext.Friendships.Count(friendship =>
                    friendship.Status == FriendshipStatus.Accepted &&
                    (friendship.RequesterUserId == user.Id || friendship.AddresseeUserId == user.Id)),
                IsFollowing = dbContext.Follows.Any(follow =>
                    follow.FollowerId == currentUserId &&
                    follow.FollowingId == user.Id),
                Friendship = dbContext.Friendships
                    .Where(friendship =>
                        (friendship.RequesterUserId == currentUserId && friendship.AddresseeUserId == user.Id) ||
                        (friendship.RequesterUserId == user.Id && friendship.AddresseeUserId == currentUserId))
                    .Select(friendship => new
                    {
                        friendship.Status,
                        IsRequester = friendship.RequesterUserId == currentUserId
                    })
                    .FirstOrDefault(),
                ConversationId = dbContext.Conversations
                    .Where(conversation =>
                        conversation.ConversationType == ConversationType.Private &&
                        conversation.Members.Any(member => member.UserId == currentUserId) &&
                        conversation.Members.Any(member => member.UserId == user.Id))
                    .OrderByDescending(conversation => conversation.CreatedAt)
                    .Select(conversation => (Guid?)conversation.Id)
                    .FirstOrDefault()
            })
            .Select(item => new UserProfileSummaryResponse(
                item.User.Id,
                item.User.DisplayName,
                item.User.AvatarUrl,
                item.User.CoverUrl,
                item.User.Gender,
                item.User.IsVerified,
                item.PostCount,
                item.FollowerCount,
                item.FollowingCount,
                item.FriendCount,
                item.IsFollowing,
                item.Friendship == null
                    ? new UserProfileFriendshipResponse("None", false)
                    : new UserProfileFriendshipResponse(
                        item.Friendship.Status == FriendshipStatus.Pending ? "Pending" :
                        item.Friendship.Status == FriendshipStatus.Accepted ? "Accepted" :
                        item.Friendship.Status == FriendshipStatus.Rejected ? "Rejected" :
                        item.Friendship.Status == FriendshipStatus.Cancelled ? "Cancelled" :
                        item.Friendship.Status == FriendshipStatus.Blocked ? "Blocked" : "None",
                        item.Friendship.IsRequester),
                (item.User.Settings == null || item.User.Settings.AllowMessageEveryone) ||
                    (item.Friendship != null && item.Friendship.Status == FriendshipStatus.Accepted),
                item.ConversationId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task AddFollowAsync(Follow follow, CancellationToken cancellationToken) =>
        dbContext.Follows.AddAsync(follow, cancellationToken).AsTask();

    public void RemoveFollow(Follow follow) => dbContext.Follows.Remove(follow);

    public Task AddFriendshipAsync(Friendship friendship, CancellationToken cancellationToken) =>
        dbContext.Friendships.AddAsync(friendship, cancellationToken).AsTask();

    public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken) =>
        dbContext.Conversations.AddAsync(conversation, cancellationToken).AsTask();

    public Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken) =>
        dbContext.Notifications.AddAsync(notification, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
