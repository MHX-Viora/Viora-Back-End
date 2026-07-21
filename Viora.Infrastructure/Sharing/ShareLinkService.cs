using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Viora.Application.Sharing;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sharing;

public sealed class ShareLinkService(AppDbContext db, IConfiguration configuration) : IShareLinkService
{
    private readonly string baseUrl = (configuration["ShareLinks:BaseUrl"] ?? "https://viora.app").TrimEnd('/');

    public async Task<ShareLinkResult<ShareLinkResponse>> GetUserShareLinkAsync(Guid viewerUserId, Guid userId, CancellationToken token)
    {
        var exists = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId &&
            user.Account.Status == AccountStatus.Active &&
            user.Account.DeletedAt == null,
            token);

        return exists
            ? ShareLinkResult<ShareLinkResponse>.Success(new ShareLinkResponse($"{baseUrl}/user/{userId}"))
            : ShareLinkResult<ShareLinkResponse>.Failure(ShareLinkError.NotFound, "Khong tim thay nguoi dung.");
    }

    public async Task<ShareLinkResult<ShareLinkResponse>> GetPostShareLinkAsync(Guid viewerUserId, Guid postId, CancellationToken token)
    {
        var validation = await ValidatePostAsync(viewerUserId, postId, PostType.Post, token);
        return validation is null
            ? ShareLinkResult<ShareLinkResponse>.Success(new ShareLinkResponse($"{baseUrl}/post/{postId}"))
            : ShareLinkResult<ShareLinkResponse>.Failure(validation.Value.Error, validation.Value.Message);
    }

    public async Task<ShareLinkResult<ShareLinkResponse>> GetReelShareLinkAsync(Guid viewerUserId, Guid reelId, CancellationToken token)
    {
        var validation = await ValidatePostAsync(viewerUserId, reelId, PostType.ShortVideo, token);
        return validation is null
            ? ShareLinkResult<ShareLinkResponse>.Success(new ShareLinkResponse($"{baseUrl}/reel/{reelId}"))
            : ShareLinkResult<ShareLinkResponse>.Failure(validation.Value.Error, validation.Value.Message);
    }

    public async Task<ShareLinkResult<GroupShareLinkResponse>> GetGroupShareLinkAsync(Guid viewerUserId, Guid groupId, CancellationToken token)
    {
        var group = await db.Conversations
            .SingleOrDefaultAsync(conversation =>
                conversation.Id == groupId &&
                conversation.ConversationType == ConversationType.Group,
                token);

        if (group is null) return ShareLinkResult<GroupShareLinkResponse>.Failure(ShareLinkError.NotFound, "Khong tim thay nhom.");
        if (group.DeletedAt.HasValue) return ShareLinkResult<GroupShareLinkResponse>.Failure(ShareLinkError.Dissolved, "Conversation has been dissolved.");

        var isMember = await db.ConversationMembers.AsNoTracking().AnyAsync(member =>
            member.ConversationId == groupId &&
            member.UserId == viewerUserId &&
            member.Status == ConversationMemberStatus.Active,
            token);
        if (!isMember) return ShareLinkResult<GroupShareLinkResponse>.Failure(ShareLinkError.Forbidden, "Ban khong phai thanh vien cua nhom.");

        if (string.IsNullOrWhiteSpace(group.InviteCode))
        {
            group.InviteCode = await db.CreateUniqueInviteCodeAsync(token);
            await db.SaveChangesAsync(token);
        }

        return ShareLinkResult<GroupShareLinkResponse>.Success(new GroupShareLinkResponse(group.InviteCode, $"{baseUrl}/group/{group.InviteCode}"));
    }

    private async Task<(ShareLinkError Error, string Message)?> ValidatePostAsync(Guid viewerUserId, Guid postId, PostType postType, CancellationToken token)
    {
        var post = await db.Posts.AsNoTracking()
            .Where(x => x.Id == postId)
            .Select(x => new
            {
                x.UserId,
                x.PostType,
                x.Status,
                x.Visibility,
                x.DeletedAt,
                IsFollower = db.Follows.Any(follow =>
                    follow.FollowerId == viewerUserId &&
                    follow.FollowingId == x.UserId)
            })
            .SingleOrDefaultAsync(token);

        if (post is null || post.PostType != postType || post.Status == PostStatus.Deleted || post.DeletedAt is not null)
        {
            return (ShareLinkError.NotFound, postType == PostType.ShortVideo ? "Khong tim thay video." : "Khong tim thay bai viet.");
        }

        var canView = post.Status == PostStatus.Published &&
            (post.UserId == viewerUserId ||
             post.Visibility == PostVisibility.Public ||
             (post.Visibility == PostVisibility.Followers && post.IsFollower));

        return canView ? null : (ShareLinkError.Forbidden, "Ban khong co quyen xem noi dung nay.");
    }
}
