using Microsoft.EntityFrameworkCore;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class VideoFeedRepository(AppDbContext dbContext) : IVideoFeedRepository
{
    // Cac trong so dung de tinh diem de xuat reel. Tang/giam cac so nay se doi do uu tien cua feed.
    private const int FollowedAuthorWeight = 900;
    private const int InterestedHashtagWeight = 350;
    private const int ReactionWeight = 5;
    private const int CommentWeight = 7;
    private const int ShareWeight = 9;
    private const int SaveWeight = 6;
    private const int ViewWeight = 1;
    private const int WatchedPenalty = 80;
    private const int CompletedPenalty = 160;
    private const int WatchDurationPenaltyDivisor = 2;

    // Ham chinh lay danh sach reel: loc du lieu, tinh diem/sap xep, phan trang va project sang DTO.
    public async Task<VideoFeedResponse> GetShortVideosAsync(
        GetShortVideosQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var sort = query.Sort.Trim().ToLowerInvariant();
        var viewerUserId = query.ViewerUserId;
        var now = DateTime.UtcNow;
        var oneDayAgo = now.AddDays(-1);
        var threeDaysAgo = now.AddDays(-3);
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);

        var videos = BuildVisibleShortVideosQuery(viewerUserId);
        videos = ApplyKeyword(videos, query.Keyword);
        videos = ApplySortFilter(videos, sort, query.UserId, viewerUserId);

        var totalItems = await videos.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var hasBehavior = await HasVideoBehaviorAsync(viewerUserId, cancellationToken);

        // Tao projection trung gian gom Post va cac tin hieu ca nhan hoa cua viewer de dung khi sort.
        var ranked = videos.Select(post => new VideoRankProjection
        {
            Post = post,
            IsFollowing = dbContext.Follows.Any(follow =>
                follow.FollowerId == viewerUserId &&
                follow.FollowingId == post.UserId),
            IsReacted = dbContext.PostReactions.Any(reaction =>
                reaction.UserId == viewerUserId &&
                reaction.PostId == post.Id),
            ReactionType = dbContext.PostReactions
                .Where(reaction => reaction.UserId == viewerUserId && reaction.PostId == post.Id)
                .Select(reaction => (ReactionType?)reaction.ReactionType)
                .FirstOrDefault(),
            IsSaved = dbContext.SavedPosts.Any(saved =>
                saved.UserId == viewerUserId &&
                saved.PostId == post.Id),
            ViewerWatchCount = dbContext.ViewHistories.Count(view =>
                view.UserId == viewerUserId &&
                view.PostId == post.Id),
            ViewerCompletedCount = dbContext.ViewHistories.Count(view =>
                view.UserId == viewerUserId &&
                view.PostId == post.Id &&
                view.IsCompleted),
            ViewerWatchDuration = dbContext.ViewHistories
                .Where(view => view.UserId == viewerUserId && view.PostId == post.Id)
                .Sum(view => (int?)view.WatchDuration) ?? 0,
            HasInterestedHashtag = dbContext.PostHashtags.Any(videoTag =>
                videoTag.PostId == post.Id &&
                dbContext.PostHashtags.Any(historyTag =>
                    historyTag.HashtagId == videoTag.HashtagId &&
                    dbContext.ViewHistories.Any(view =>
                        view.UserId == viewerUserId &&
                        view.PostId == historyTag.PostId)))
        });

        ranked = ApplySort(ranked, sort, hasBehavior, oneDayAgo, threeDaysAgo, sevenDaysAgo, thirtyDaysAgo);

        // Phan trang sau khi da sap xep, roi chi lay cac field FE can hien thi.
        var items = await ranked
            .Skip(skip)
            .Take(pageSize)
            .Select(item => new VideoFeedItemResponse(
                item.Post.Id,
                item.Post.Content,
                item.Post.Location,
                item.Post.CreatedAt,
                item.Post.ViewCount,
                item.Post.ReactionCount,
                item.Post.CommentCount,
                item.Post.ShareCount,
                item.Post.SaveCount,
                item.IsSaved,
                item.IsReacted,
                item.ReactionType,
                item.Post.Media
                    .OrderBy(media => media.CreatedAt)
                    .Select(media => new VideoFeedMediaResponse(
                        media.Id,
                        media.MediaUrl,
                        media.ThumbnailUrl))
                    .ToList(),
                dbContext.PostHashtags
                    .Where(postHashtag => postHashtag.PostId == item.Post.Id)
                    .OrderBy(postHashtag => postHashtag.Hashtag.Name)
                    .Select(postHashtag => postHashtag.Hashtag.Name)
                    .ToList(),
                new VideoFeedUserResponse(
                    item.Post.User.Id,
                    item.Post.User.DisplayName,
                    item.Post.User.AvatarUrl,
                    item.Post.User.IsVerified,
                    item.IsFollowing)))
            .ToListAsync(cancellationToken);

        return new VideoFeedResponse(page, pageSize, totalItems, totalPages, items);
    }

    // Tao query goc cho reel co the xem duoc: ShortVideo, Published, user active, dung visibility.
    private IQueryable<Post> BuildVisibleShortVideosQuery(Guid viewerUserId) =>
        dbContext.Posts
            .AsNoTracking()
            .Where(post =>
                post.PostType == PostType.ShortVideo &&
                post.Status == PostStatus.Published &&
                post.DeletedAt == null &&
                post.User.Account.Status == AccountStatus.Active &&
                post.User.Account.DeletedAt == null &&
                (post.Visibility == PostVisibility.Public ||
                    post.UserId == viewerUserId ||
                    (post.Visibility == PostVisibility.Followers &&
                        dbContext.Follows.Any(follow =>
                            follow.FollowerId == viewerUserId &&
                            follow.FollowingId == post.UserId))));

    // Loc theo keyword tren content, ten nguoi dang va hashtag. Hashtag co the truyen co hoac khong co dau #.
    private IQueryable<Post> ApplyKeyword(IQueryable<Post> videos, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return videos;
        }

        var trimmed = keyword.Trim();
        var hashtagKeyword = trimmed.TrimStart('#');
        var textPattern = $"%{trimmed}%";
        var hashtagPattern = $"%{hashtagKeyword}%";

        return videos.Where(post =>
            (post.Content != null && EF.Functions.ILike(post.Content, textPattern)) ||
            EF.Functions.ILike(post.User.DisplayName, textPattern) ||
            dbContext.PostHashtags.Any(postHashtag =>
                postHashtag.PostId == post.Id &&
                EF.Functions.ILike(postHashtag.Hashtag.Name, hashtagPattern)));
    }

    // Ap dung filter rieng theo sort: following/friends/user chi lay dung tap video tuong ung.
    private IQueryable<Post> ApplySortFilter(
        IQueryable<Post> videos,
        string sort,
        Guid? userId,
        Guid viewerUserId) =>
        sort switch
        {
            "following" => videos.Where(post => dbContext.Follows.Any(follow =>
                follow.FollowerId == viewerUserId &&
                follow.FollowingId == post.UserId)),
            "friends" => videos.Where(post => dbContext.Friendships.Any(friendship =>
                friendship.Status == FriendshipStatus.Accepted &&
                ((friendship.RequesterUserId == viewerUserId && friendship.AddresseeUserId == post.UserId) ||
                    (friendship.AddresseeUserId == viewerUserId && friendship.RequesterUserId == post.UserId)))),
            "user" => videos.Where(post => post.UserId == userId!.Value),
            _ => videos
        };

    // Chon cach sap xep theo sort. Recommend co 2 nhanh: user da co hanh vi va cold start.
    private static IOrderedQueryable<VideoRankProjection> ApplySort(
        IQueryable<VideoRankProjection> ranked,
        string sort,
        bool hasBehavior,
        DateTime oneDayAgo,
        DateTime threeDaysAgo,
        DateTime sevenDaysAgo,
        DateTime thirtyDaysAgo) =>
        sort switch
        {
            "recommend" => hasBehavior
                ? ApplyRecommendSort(ranked, oneDayAgo, threeDaysAgo, sevenDaysAgo)
                : ApplyColdStartSort(ranked, oneDayAgo, threeDaysAgo, sevenDaysAgo),
            "popular" => ApplyPopularSort(ranked, oneDayAgo, threeDaysAgo, sevenDaysAgo, thirtyDaysAgo),
            _ => ApplyLatestSort(ranked)
        };

    // Sap xep de xuat khi user da co hanh vi: uu tien follow/hashtag quan tam/tuong tac/do moi,
    // dong thoi tru diem video da xem nhieu, da xem het hoac da xem lau.
    private static IOrderedQueryable<VideoRankProjection> ApplyRecommendSort(
        IQueryable<VideoRankProjection> ranked,
        DateTime oneDayAgo,
        DateTime threeDaysAgo,
        DateTime sevenDaysAgo) =>
        ranked
            .OrderByDescending(item =>
                (item.IsFollowing ? FollowedAuthorWeight : 0) +
                (item.HasInterestedHashtag ? InterestedHashtagWeight : 0) +
                (item.Post.CreatedAt >= oneDayAgo ? 260 :
                    item.Post.CreatedAt >= threeDaysAgo ? 150 :
                    item.Post.CreatedAt >= sevenDaysAgo ? 70 : 10) +
                item.Post.ReactionCount * ReactionWeight +
                item.Post.CommentCount * CommentWeight +
                item.Post.ShareCount * ShareWeight +
                item.Post.SaveCount * SaveWeight +
                item.Post.ViewCount * ViewWeight -
                item.ViewerWatchCount * WatchedPenalty -
                item.ViewerCompletedCount * CompletedPenalty -
                item.ViewerWatchDuration / WatchDurationPenaltyDivisor)
            .ThenByDescending(item => item.Post.CreatedAt)
            .ThenBy(item => item.Post.Id);

    // Sap xep cho user chua co hanh vi: khong ca nhan hoa, chi dua tren do moi va do pho bien.
    private static IOrderedQueryable<VideoRankProjection> ApplyColdStartSort(
        IQueryable<VideoRankProjection> ranked,
        DateTime oneDayAgo,
        DateTime threeDaysAgo,
        DateTime sevenDaysAgo) =>
        ranked
            .OrderByDescending(item =>
                (item.Post.CreatedAt >= oneDayAgo ? 260 :
                    item.Post.CreatedAt >= threeDaysAgo ? 150 :
                    item.Post.CreatedAt >= sevenDaysAgo ? 70 : 10) +
                item.Post.ReactionCount * ReactionWeight +
                item.Post.CommentCount * CommentWeight +
                item.Post.ShareCount * ShareWeight +
                item.Post.SaveCount * SaveWeight +
                item.Post.ViewCount * ViewWeight)
            .ThenByDescending(item => item.Post.CreatedAt)
            .ThenBy(item => item.Post.Id);

    // Sap xep video noi bat: uu tien tong tuong tac va cong diem nhe cho video moi de tranh video cu chiem feed.
    private static IOrderedQueryable<VideoRankProjection> ApplyPopularSort(
        IQueryable<VideoRankProjection> ranked,
        DateTime oneDayAgo,
        DateTime threeDaysAgo,
        DateTime sevenDaysAgo,
        DateTime thirtyDaysAgo) =>
        ranked
            .OrderByDescending(item =>
                item.Post.ReactionCount * ReactionWeight +
                item.Post.CommentCount * CommentWeight +
                item.Post.ShareCount * ShareWeight +
                item.Post.SaveCount * SaveWeight +
                item.Post.ViewCount * ViewWeight +
                (item.Post.CreatedAt >= oneDayAgo ? 160 :
                    item.Post.CreatedAt >= threeDaysAgo ? 90 :
                    item.Post.CreatedAt >= sevenDaysAgo ? 45 :
                    item.Post.CreatedAt >= thirtyDaysAgo ? 15 : 0))
            .ThenByDescending(item => item.Post.CreatedAt)
            .ThenBy(item => item.Post.Id);

    // Sap xep moi nhat truoc. Dung cho latest, following, friends va user sau khi da filter tap video.
    private static IOrderedQueryable<VideoRankProjection> ApplyLatestSort(IQueryable<VideoRankProjection> ranked) =>
        ranked
            .OrderByDescending(item => item.Post.CreatedAt)
            .ThenBy(item => item.Post.Id);

    // Kiem tra user da co du lieu hanh vi hay chua de quyet dinh dung recommend ca nhan hoa hay cold start.
    private async Task<bool> HasVideoBehaviorAsync(Guid viewerUserId, CancellationToken cancellationToken) =>
        await dbContext.Follows.AsNoTracking().AnyAsync(follow => follow.FollowerId == viewerUserId, cancellationToken) ||
        await dbContext.ViewHistories.AsNoTracking().AnyAsync(view => view.UserId == viewerUserId, cancellationToken) ||
        await dbContext.PostReactions.AsNoTracking().AnyAsync(reaction => reaction.UserId == viewerUserId, cancellationToken) ||
        await dbContext.SavedPosts.AsNoTracking().AnyAsync(saved => saved.UserId == viewerUserId, cancellationToken);

    // DTO noi bo chi dung trong query de gom cac tin hieu ranking truoc khi project sang response.
    private sealed class VideoRankProjection
    {
        public Post Post { get; init; } = null!;
        public bool IsFollowing { get; init; }
        public bool IsReacted { get; init; }
        public ReactionType? ReactionType { get; init; }
        public bool IsSaved { get; init; }
        public int ViewerWatchCount { get; init; }
        public int ViewerCompletedCount { get; init; }
        public int ViewerWatchDuration { get; init; }
        public bool HasInterestedHashtag { get; init; }
    }
}
