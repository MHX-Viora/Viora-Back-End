using Microsoft.EntityFrameworkCore;
using Viora.Application.Admin;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class AdminRepository(AppDbContext dbContext) : IAdminRepository
{
    public async Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        return new AdminDashboardResponse(
            await dbContext.Users.AsNoTracking().CountAsync(cancellationToken),
            await dbContext.Accounts.AsNoTracking().CountAsync(x => x.LastLoginAt >= today, cancellationToken),
            await dbContext.Users.AsNoTracking().CountAsync(x => x.CreatedAt >= today, cancellationToken),
            await dbContext.Posts.AsNoTracking().CountAsync(x => x.PostType == PostType.Post, cancellationToken),
            await dbContext.Posts.AsNoTracking().CountAsync(x => x.PostType == PostType.ShortVideo, cancellationToken),
            await dbContext.Comments.AsNoTracking().CountAsync(cancellationToken),
            await dbContext.Conversations.AsNoTracking().CountAsync(cancellationToken),
            await dbContext.Reports.AsNoTracking().CountAsync(x => x.Status == ReportStatus.Pending, cancellationToken),
            await dbContext.UserIdentities.AsNoTracking().CountAsync(x => x.Status == IdentitySubmissionStatus.Pending, cancellationToken),
            await dbContext.Posts.AsNoTracking().CountAsync(x => x.PostType == PostType.Post && x.CreatedAt >= today, cancellationToken),
            await dbContext.Posts.AsNoTracking().CountAsync(x => x.PostType == PostType.ShortVideo && x.CreatedAt >= today, cancellationToken));
    }

    public async Task<AdminPagedResponse<AdminUserSummaryResponse>> GetUsersAsync(GetAdminUsersQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            source = source.Where(x =>
                x.DisplayName.ToLower().Contains(keyword) ||
                (x.Account.Email != null && x.Account.Email.ToLower().Contains(keyword)) ||
                (x.Account.Phone != null && x.Account.Phone.ToLower().Contains(keyword)));
        }
        if (query.Status is not null) source = source.Where(x => x.Account.Status == query.Status);
        if (query.IdentityStatus is not null) source = source.Where(x => x.IdentityStatus == query.IdentityStatus);
        if (query.IsVerified is not null) source = source.Where(x => x.IsVerified == query.IsVerified);

        source = query.SortBy?.ToLowerInvariant() switch
        {
            "name" or "displayname" => SortAsc(query.SortDirection) ? source.OrderBy(x => x.DisplayName) : source.OrderByDescending(x => x.DisplayName),
            "createdat" => SortAsc(query.SortDirection) ? source.OrderBy(x => x.CreatedAt) : source.OrderByDescending(x => x.CreatedAt),
            _ => source.OrderByDescending(x => x.CreatedAt)
        };

        return await PageAsync(
            source.Select(x => new AdminUserSummaryResponse(
                x.Id,
                x.DisplayName,
                x.AvatarUrl,
                x.Account.Email,
                x.Account.Phone,
                x.Account.Status,
                x.IdentityStatus,
                x.IsVerified,
                dbContext.Posts.Count(p => p.UserId == x.Id && p.PostType == PostType.Post),
                dbContext.Friendships.Count(f => f.Status == FriendshipStatus.Accepted && (f.RequesterUserId == x.Id || f.AddresseeUserId == x.Id)),
                x.CreatedAt)),
            query.Page,
            query.PageSize,
            cancellationToken);
    }

    public async Task<AdminUserDetailResponse?> GetUserDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminUserDetailResponse(
                x.Id,
                x.AccountId,
                x.DisplayName,
                x.AvatarUrl,
                x.CoverUrl,
                x.Account.Email,
                x.Account.Phone,
                x.Account.Role,
                x.Account.Status,
                x.IdentityStatus,
                x.IsVerified,
                dbContext.Posts.Count(p => p.UserId == x.Id && p.PostType == PostType.Post),
                dbContext.Posts.Count(p => p.UserId == x.Id && p.PostType == PostType.ShortVideo),
                dbContext.Friendships.Count(f => f.Status == FriendshipStatus.Accepted && (f.RequesterUserId == x.Id || f.AddresseeUserId == x.Id)),
                dbContext.Follows.Count(f => f.FollowingId == x.Id),
                dbContext.Follows.Count(f => f.FollowerId == x.Id),
                dbContext.Reports.Count(r => r.TargetType == ReportTargetType.User && r.TargetId == x.Id),
                x.CreatedAt,
                x.Account.LastLoginAt,
                x.IdentitySubmissions.OrderByDescending(i => i.CreatedAt)
                    .Select(i => new AdminIdentityDetailResponse(i.Id, i.UserId, x.DisplayName, x.AvatarUrl, i.FullName, i.Birthday, i.IdentityNumber, i.FrontImageUrl, i.BackImageUrl, i.Status, i.RejectReason, i.CreatedAt, i.ReviewedAt))
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UpdateUserStatusAsync(Guid adminId, Guid id, AccountStatus status, string? reason, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return false;
        user.Account.Status = status;
        if (status == AccountStatus.Deleted) user.Account.DeletedAt = DateTime.UtcNow;
        AddLog(adminId, "UpdateUserStatus", "User", id, reason ?? $"Status={status}");
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateUserVerifyAsync(Guid adminId, Guid id, bool isVerified, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return false;
        user.IsVerified = isVerified;
        AddLog(adminId, "UpdateUserVerify", "User", id, $"IsVerified={isVerified}");
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminPagedResponse<AdminIdentitySummaryResponse>> GetIdentitiesAsync(GetAdminIdentitiesQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.UserIdentities.AsNoTracking();
        if (query.Status is not null) source = source.Where(x => x.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            source = source.Where(x => x.FullName.ToLower().Contains(keyword) || x.IdentityNumber.ToLower().Contains(keyword) || x.User.DisplayName.ToLower().Contains(keyword));
        }
        source = SortAsc(query.SortDirection) ? source.OrderBy(x => x.CreatedAt) : source.OrderByDescending(x => x.CreatedAt);
        return await PageAsync(source.Select(x => new AdminIdentitySummaryResponse(x.Id, x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.FullName, x.IdentityNumber, x.Status, x.CreatedAt, x.ReviewedAt)), query.Page, query.PageSize, cancellationToken);
    }

    public async Task<AdminIdentityDetailResponse?> GetIdentityDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.UserIdentities.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminIdentityDetailResponse(x.Id, x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.FullName, x.Birthday, x.IdentityNumber, x.FrontImageUrl, x.BackImageUrl, x.Status, x.RejectReason, x.CreatedAt, x.ReviewedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ApproveIdentityAsync(Guid adminId, Guid id, CancellationToken cancellationToken)
    {
        var identity = await dbContext.UserIdentities.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (identity is null) return false;
        identity.Status = IdentitySubmissionStatus.Approved;
        identity.ReviewedBy = adminId;
        identity.ReviewedAt = DateTime.UtcNow;
        identity.User.IdentityStatus = UserIdentityState.Verified;
        identity.User.IsVerified = true;
        AddLog(adminId, "ApproveIdentity", "Identity", id, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RejectIdentityAsync(Guid adminId, Guid id, string? reason, CancellationToken cancellationToken)
    {
        var identity = await dbContext.UserIdentities.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (identity is null) return false;
        identity.Status = IdentitySubmissionStatus.Rejected;
        identity.ReviewedBy = adminId;
        identity.ReviewedAt = DateTime.UtcNow;
        identity.RejectReason = reason;
        identity.User.IdentityStatus = UserIdentityState.Rejected;
        AddLog(adminId, "RejectIdentity", "Identity", id, reason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminPagedResponse<AdminPostSummaryResponse>> GetPostsAsync(GetAdminPostsQuery query, PostType postType, CancellationToken cancellationToken)
    {
        var source = dbContext.Posts.AsNoTracking().Where(x => x.PostType == postType);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            source = source.Where(x => (x.Content != null && x.Content.ToLower().Contains(keyword)) || x.User.DisplayName.ToLower().Contains(keyword));
        }
        if (query.UserId is not null) source = source.Where(x => x.UserId == query.UserId);
        if (query.Status is not null) source = source.Where(x => x.Status == query.Status);
        if (query.Reported == true) source = source.Where(x => dbContext.Reports.Any(r => r.TargetType == ReportTargetType.Post && r.TargetId == x.Id));
        source = SortAsc(query.SortDirection) ? source.OrderBy(x => x.CreatedAt) : source.OrderByDescending(x => x.CreatedAt);
        return await PageAsync(source.Select(x => new AdminPostSummaryResponse(x.Id, x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.PostType, x.Content, x.Status, x.ReactionCount, x.CommentCount, x.ShareCount, dbContext.Reports.Count(r => r.TargetType == ReportTargetType.Post && r.TargetId == x.Id), x.CreatedAt)), query.Page, query.PageSize, cancellationToken);
    }

    public async Task<AdminPostDetailResponse?> GetPostDetailAsync(Guid id, PostType? postType, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.AsNoTracking()
            .Where(x => x.Id == id && (postType == null || x.PostType == postType))
            .Select(x => new AdminPostDetailResponse(
                x.Id, x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.PostType, x.Content, x.Location, x.Visibility, x.Status,
                x.ReactionCount, x.CommentCount, x.ShareCount, x.SaveCount, x.ViewCount,
                dbContext.Reports.Count(r => r.TargetType == ReportTargetType.Post && r.TargetId == x.Id),
                x.CreatedAt,
                x.Media.Select(m => new AdminPostMediaResponse(m.Id, m.MediaUrl, m.ThumbnailUrl)).ToList(),
                dbContext.PostHashtags.Where(h => h.PostId == x.Id).Select(h => h.Hashtag.Name).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
        return post;
    }

    public async Task<bool> SetPostStatusAsync(Guid adminId, Guid id, PostStatus status, PostType? postType, string action, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.FirstOrDefaultAsync(x => x.Id == id && (postType == null || x.PostType == postType), cancellationToken);
        if (post is null) return false;
        post.Status = status;
        post.DeletedAt = status == PostStatus.Deleted ? DateTime.UtcNow : null;
        AddLog(adminId, action, post.PostType == PostType.ShortVideo ? "Video" : "Post", id, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminPagedResponse<AdminReportSummaryResponse>> GetReportsAsync(GetAdminReportsQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.Reports.AsNoTracking();
        if (query.Status is not null) source = source.Where(x => x.Status == query.Status);
        if (query.TargetType is not null) source = source.Where(x => x.TargetType == query.TargetType);
        if (query.Reason is not null) source = source.Where(x => x.Reason == query.Reason);
        source = SortAsc(query.SortDirection) ? source.OrderBy(x => x.CreatedAt) : source.OrderByDescending(x => x.CreatedAt);
        return await PageAsync(source.Select(x => new AdminReportSummaryResponse(x.Id, x.ReporterUserId, x.ReporterUser.DisplayName, x.ReporterUser.AvatarUrl, x.TargetId, x.TargetType, x.Reason, x.Status, x.Description, x.CreatedAt)), query.Page, query.PageSize, cancellationToken);
    }

    public async Task<AdminReportDetailResponse?> GetReportDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var summary = await dbContext.Reports.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminReportSummaryResponse(x.Id, x.ReporterUserId, x.ReporterUser.DisplayName, x.ReporterUser.AvatarUrl, x.TargetId, x.TargetType, x.Reason, x.Status, x.Description, x.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return null;

        object? target = summary.TargetType switch
        {
            ReportTargetType.User => await GetUserDetailAsync(summary.TargetId, cancellationToken),
            ReportTargetType.Post => await GetPostDetailAsync(summary.TargetId, null, cancellationToken),
            ReportTargetType.Comment => await dbContext.Comments.AsNoTracking().Where(x => x.Id == summary.TargetId).Select(x => new { x.Id, x.PostId, x.UserId, x.Content, x.Status, x.CreatedAt }).FirstOrDefaultAsync(cancellationToken),
            ReportTargetType.Message => await dbContext.Messages.AsNoTracking().Where(x => x.Id == summary.TargetId).Select(x => new { x.Id, x.ConversationId, x.SenderUserId, x.Content, x.MessageType, x.IsDeleted, x.CreatedAt }).FirstOrDefaultAsync(cancellationToken),
            _ => null
        };
        return new AdminReportDetailResponse(id, summary, target);
    }

    public async Task<bool> ApproveReportAsync(Guid adminId, Guid id, string? action, CancellationToken cancellationToken)
    {
        var report = await dbContext.Reports.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null) return false;
        report.Status = ReportStatus.Approved;
        report.ReviewedBy = adminId;
        report.ReviewedAt = DateTime.UtcNow;
        await ApplyReportActionAsync(report, action, cancellationToken);
        AddLog(adminId, "ApproveReport", "Report", id, action);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RejectReportAsync(Guid adminId, Guid id, CancellationToken cancellationToken)
    {
        var report = await dbContext.Reports.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null) return false;
        report.Status = ReportStatus.Rejected;
        report.ReviewedBy = adminId;
        report.ReviewedAt = DateTime.UtcNow;
        AddLog(adminId, "RejectReport", "Report", id, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AdminPagedResponse<AdminHashtagSummaryResponse>> GetHashtagsAsync(GetAdminHashtagsQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.Hashtags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            source = source.Where(x => x.Name.ToLower().Contains(keyword));
        }
        source = query.SortBy?.ToLowerInvariant() == "name"
            ? (SortAsc(query.SortDirection) ? source.OrderBy(x => x.Name) : source.OrderByDescending(x => x.Name))
            : (SortAsc(query.SortDirection) ? source.OrderBy(x => x.CreatedAt) : source.OrderByDescending(x => x.CreatedAt));
        return await PageAsync(source.Select(x => new AdminHashtagSummaryResponse(x.Id, x.Name, x.PostCount, x.CreatedAt)), query.Page, query.PageSize, cancellationToken);
    }

    public async Task<bool?> RenameHashtagAsync(Guid adminId, Guid id, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().TrimStart('#').ToLowerInvariant();
        var hashtag = await dbContext.Hashtags.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (hashtag is null) return null;
        if (await dbContext.Hashtags.AnyAsync(x => x.Id != id && x.Name == normalized, cancellationToken)) return false;
        hashtag.Name = normalized;
        AddLog(adminId, "RenameHashtag", "Hashtag", id, normalized);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteHashtagAsync(Guid adminId, Guid id, CancellationToken cancellationToken)
    {
        var hashtag = await dbContext.Hashtags.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (hashtag is null) return false;
        dbContext.Hashtags.Remove(hashtag);
        AddLog(adminId, "DeleteHashtag", "Hashtag", id, hashtag.Name);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task CreateAnnouncementAsync(Guid adminId, string title, string content, string? imageUrl, string sendTo, CancellationToken cancellationToken)
    {
        var users = dbContext.Users.AsNoTracking();
        users = sendTo.Equals("Verified", StringComparison.OrdinalIgnoreCase) ? users.Where(x => x.IsVerified) :
            sendTo.Equals("Unverified", StringComparison.OrdinalIgnoreCase) ? users.Where(x => !x.IsVerified) : users;

        var notifications = await users.Select(x => new Notification
        {
            UserId = x.Id,
            SenderUserId = adminId,
            NotificationType = NotificationType.AdminAnnouncement,
            Title = title,
            Content = content,
            ImageUrl = imageUrl,
            ReferenceType = NotificationReferenceType.User,
            ReferenceId = adminId
        }).ToListAsync(cancellationToken);

        dbContext.Notifications.AddRange(notifications);
        AddLog(adminId, "CreateAnnouncement", "Notification", null, $"SendTo={sendTo}; Count={notifications.Count}");
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminPagedResponse<AdminConversationSummaryResponse>> GetConversationsAsync(GetAdminConversationsQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.Conversations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            source = source.Where(x => (x.Name != null && x.Name.ToLower().Contains(keyword)) || x.InviteCode.ToLower().Contains(keyword));
        }
        if (query.Type is not null) source = source.Where(x => x.ConversationType == query.Type);
        source = SortAsc(query.SortDirection) ? source.OrderBy(x => x.UpdatedAt) : source.OrderByDescending(x => x.UpdatedAt);
        return await PageAsync(source.Select(x => new AdminConversationSummaryResponse(
            x.Id, x.ConversationType, x.Name, x.AvatarUrl,
            dbContext.ConversationMembers.Count(m => m.ConversationId == x.Id && (m.Status == null || m.Status == ConversationMemberStatus.Active)),
            dbContext.Messages.Count(m => m.ConversationId == x.Id),
            x.UpdatedAt,
            x.LastMessageAt)), query.Page, query.PageSize, cancellationToken);
    }

    public async Task<AdminConversationDetailResponse?> GetConversationDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.ConversationType, x.Name, x.AvatarUrl, x.InviteCode, x.CreatedAt, x.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null) return null;

        var members = await dbContext.ConversationMembers.AsNoTracking()
            .Where(x => x.ConversationId == id)
            .OrderBy(x => x.JoinedAt)
            .Select(x => new AdminConversationMemberResponse(x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.Role, x.Status, x.JoinedAt))
            .ToListAsync(cancellationToken);

        var messages = await dbContext.Messages.AsNoTracking()
            .Where(x => x.ConversationId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new AdminMessageResponse(x.Id, x.SenderUserId, x.SenderUser.DisplayName, x.MessageType, x.Content, x.IsDeleted, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AdminConversationDetailResponse(conversation.Id, conversation.ConversationType, conversation.Name, conversation.AvatarUrl, conversation.InviteCode, members.Count, conversation.CreatedAt, conversation.UpdatedAt, members, messages);
    }

    public async Task<AdminPagedResponse<AdminLogSummaryResponse>> GetLogsAsync(GetAdminLogsQuery query, CancellationToken cancellationToken)
    {
        var source = dbContext.AdminLogs.AsNoTracking();
        if (query.AdminId is not null) source = source.Where(x => x.AdminId == query.AdminId);
        if (!string.IsNullOrWhiteSpace(query.Action)) source = source.Where(x => x.Action == query.Action);
        if (query.From is not null) source = source.Where(x => x.CreatedAt >= query.From);
        if (query.To is not null) source = source.Where(x => x.CreatedAt <= query.To);
        source = SortAsc(query.SortDirection) ? source.OrderBy(x => x.CreatedAt) : source.OrderByDescending(x => x.CreatedAt);
        return await PageAsync(source.Select(x => new AdminLogSummaryResponse(x.Id, x.AdminId, x.Admin.DisplayName, x.Action, x.TargetType, x.TargetId, x.Description, x.CreatedAt)), query.Page, query.PageSize, cancellationToken);
    }

    private async Task ApplyReportActionAsync(Report report, string? action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "HidePost":
                await SetReportedPostStatusAsync(report, PostStatus.Hidden, cancellationToken);
                break;
            case "DeletePost":
                await SetReportedPostStatusAsync(report, PostStatus.Deleted, cancellationToken);
                break;
            case "BanUser" when report.TargetType == ReportTargetType.User:
                var user = await dbContext.Users.Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == report.TargetId, cancellationToken);
                if (user is not null) user.Account.Status = AccountStatus.Banned;
                break;
            case "DeleteComment" when report.TargetType == ReportTargetType.Comment:
                var comment = await dbContext.Comments.FirstOrDefaultAsync(x => x.Id == report.TargetId, cancellationToken);
                if (comment is not null) comment.Status = CommentStatus.Deleted;
                break;
            case "DeleteMessage" when report.TargetType == ReportTargetType.Message:
                var message = await dbContext.Messages.FirstOrDefaultAsync(x => x.Id == report.TargetId, cancellationToken);
                if (message is not null) message.IsDeleted = true;
                break;
        }
    }

    private async Task SetReportedPostStatusAsync(Report report, PostStatus status, CancellationToken cancellationToken)
    {
        if (report.TargetType != ReportTargetType.Post) return;
        var post = await dbContext.Posts.FirstOrDefaultAsync(x => x.Id == report.TargetId, cancellationToken);
        if (post is null) return;
        post.Status = status;
        post.DeletedAt = status == PostStatus.Deleted ? DateTime.UtcNow : post.DeletedAt;
    }

    private void AddLog(Guid adminId, string action, string targetType, Guid? targetId, string? description)
    {
        dbContext.AdminLogs.Add(new AdminLog
        {
            AdminId = adminId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Description = description
        });
    }

    private static async Task<AdminPagedResponse<T>> PageAsync<T>(IQueryable<T> source, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var totalItems = await source.CountAsync(cancellationToken);
        var items = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new AdminPagedResponse<T>(page, pageSize, totalItems, totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize), items);
    }

    private static bool SortAsc(string? sortDirection) => sortDirection?.Equals("asc", StringComparison.OrdinalIgnoreCase) == true;
}
