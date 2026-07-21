using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Admin;

public sealed record AdminPagedResponse<T>(int Page, int PageSize, int TotalItems, int TotalPages, IReadOnlyList<T> Items);

public sealed record AdminDashboardResponse(
    int UserCount,
    int ActiveUserToday,
    int NewUserToday,
    int PostCount,
    int VideoCount,
    int CommentCount,
    int ConversationCount,
    int PendingReportCount,
    int PendingIdentityCount,
    int TodayPostCount,
    int TodayVideoCount);

public sealed record AdminUserSummaryResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string? Email,
    string? Phone,
    AccountStatus Status,
    UserIdentityState IdentityStatus,
    bool IsVerified,
    int PostCount,
    int FriendCount,
    DateTime CreatedAt);

public sealed record AdminUserDetailResponse(
    Guid Id,
    Guid AccountId,
    string DisplayName,
    string? AvatarUrl,
    string? CoverUrl,
    string? Email,
    string? Phone,
    AccountRole Role,
    AccountStatus Status,
    UserIdentityState IdentityStatus,
    bool IsVerified,
    int PostCount,
    int VideoCount,
    int FriendCount,
    int FollowerCount,
    int FollowingCount,
    int ReportCount,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    AdminIdentityDetailResponse? Identity);

public sealed record AdminIdentitySummaryResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    string FullName,
    string IdentityNumber,
    IdentitySubmissionStatus Status,
    DateTime CreatedAt,
    DateTime? ReviewedAt);

public sealed record AdminIdentityDetailResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    string FullName,
    DateOnly? Birthday,
    string IdentityNumber,
    string FrontImageUrl,
    string BackImageUrl,
    IdentitySubmissionStatus Status,
    string? RejectReason,
    DateTime CreatedAt,
    DateTime? ReviewedAt);

public sealed record AdminPostSummaryResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    PostType PostType,
    string? Content,
    PostStatus Status,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int ReportCount,
    DateTime CreatedAt);

public sealed record AdminPostDetailResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    PostType PostType,
    string? Content,
    string? Location,
    PostVisibility Visibility,
    PostStatus Status,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    int ReportCount,
    DateTime CreatedAt,
    IReadOnlyList<AdminPostMediaResponse> Media,
    IReadOnlyList<string> Hashtags);

public sealed record AdminPostMediaResponse(Guid Id, string MediaUrl, string? ThumbnailUrl);

public sealed record AdminReportSummaryResponse(
    Guid Id,
    Guid ReporterUserId,
    string ReporterDisplayName,
    string? ReporterAvatarUrl,
    Guid TargetId,
    ReportTargetType TargetType,
    ReportReason Reason,
    ReportStatus Status,
    string? Description,
    DateTime CreatedAt);

public sealed record AdminReportDetailResponse(
    Guid Id,
    AdminReportSummaryResponse Summary,
    object? Target);

public sealed record AdminHashtagSummaryResponse(Guid Id, string Name, int PostCount, DateTime CreatedAt);

public sealed record AdminConversationSummaryResponse(
    Guid Id,
    ConversationType ConversationType,
    string? Name,
    string? AvatarUrl,
    int MemberCount,
    int MessageCount,
    DateTime UpdatedAt,
    DateTime? LastMessageAt);

public sealed record AdminConversationDetailResponse(
    Guid Id,
    ConversationType ConversationType,
    string? Name,
    string? AvatarUrl,
    string InviteCode,
    int MemberCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AdminConversationMemberResponse> Members,
    IReadOnlyList<AdminMessageResponse> Messages);

public sealed record AdminConversationMemberResponse(Guid UserId, string DisplayName, string? AvatarUrl, ConversationMemberRole Role, ConversationMemberStatus? Status, DateTime JoinedAt);
public sealed record AdminMessageResponse(Guid Id, Guid SenderUserId, string SenderDisplayName, MessageType MessageType, string? Content, bool IsDeleted, DateTime CreatedAt);

public sealed record AdminLogSummaryResponse(Guid Id, Guid AdminId, string? AdminDisplayName, string Action, string TargetType, Guid? TargetId, string? Description, DateTime CreatedAt);

public sealed record AdminMutationResponse(bool Success, string Message);

public sealed record GetAdminDashboardQuery : IRequest<AdminDashboardResponse>;
public sealed record GetAdminUsersQuery(int Page, int PageSize, string? Keyword, AccountStatus? Status, UserIdentityState? IdentityStatus, bool? IsVerified, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminUserSummaryResponse>>;
public sealed record GetAdminUserDetailQuery(Guid Id) : IRequest<AdminUserDetailResponse?>;
public sealed record UpdateAdminUserStatusCommand(Guid AdminId, Guid Id, AccountStatus Status, string? Reason) : IRequest<AdminMutationResponse?>;
public sealed record UpdateAdminUserVerifyCommand(Guid AdminId, Guid Id, bool IsVerified) : IRequest<AdminMutationResponse?>;
public sealed record GetAdminIdentitiesQuery(int Page, int PageSize, string? Keyword, IdentitySubmissionStatus? Status, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminIdentitySummaryResponse>>;
public sealed record GetAdminIdentityDetailQuery(Guid Id) : IRequest<AdminIdentityDetailResponse?>;
public sealed record ApproveAdminIdentityCommand(Guid AdminId, Guid Id) : IRequest<AdminMutationResponse?>;
public sealed record RejectAdminIdentityCommand(Guid AdminId, Guid Id, string? Reason) : IRequest<AdminMutationResponse?>;
public sealed record GetAdminPostsQuery(int Page, int PageSize, string? Keyword, Guid? UserId, bool? Reported, PostStatus? Status, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminPostSummaryResponse>>;
public sealed record GetAdminVideosQuery(int Page, int PageSize, string? Keyword, Guid? UserId, bool? Reported, PostStatus? Status, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminPostSummaryResponse>>;
public sealed record GetAdminPostDetailQuery(Guid Id, PostType? PostType = null) : IRequest<AdminPostDetailResponse?>;
public sealed record HideAdminPostCommand(Guid AdminId, Guid Id, PostType? PostType = null) : IRequest<AdminMutationResponse?>;
public sealed record RestoreAdminPostCommand(Guid AdminId, Guid Id, PostType? PostType = null) : IRequest<AdminMutationResponse?>;
public sealed record DeleteAdminPostCommand(Guid AdminId, Guid Id, PostType? PostType = null) : IRequest<AdminMutationResponse?>;
public sealed record GetAdminReportsQuery(int Page, int PageSize, ReportStatus? Status, ReportTargetType? TargetType, ReportReason? Reason, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminReportSummaryResponse>>;
public sealed record GetAdminReportDetailQuery(Guid Id) : IRequest<AdminReportDetailResponse?>;
public sealed record ApproveAdminReportCommand(Guid AdminId, Guid Id, string? Action) : IRequest<AdminMutationResponse?>;
public sealed record RejectAdminReportCommand(Guid AdminId, Guid Id) : IRequest<AdminMutationResponse?>;
public sealed record GetAdminHashtagsQuery(int Page, int PageSize, string? Keyword, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminHashtagSummaryResponse>>;
public sealed record RenameAdminHashtagCommand(Guid AdminId, Guid Id, string Name) : IRequest<AdminMutationResponse?>;
public sealed record DeleteAdminHashtagCommand(Guid AdminId, Guid Id) : IRequest<AdminMutationResponse?>;
public sealed record CreateAdminAnnouncementCommand(Guid AdminId, string Title, string Content, string? ImageUrl, string SendTo) : IRequest<AdminMutationResponse>;
public sealed record GetAdminConversationsQuery(int Page, int PageSize, string? Keyword, ConversationType? Type, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminConversationSummaryResponse>>;
public sealed record GetAdminConversationDetailQuery(Guid Id) : IRequest<AdminConversationDetailResponse?>;
public sealed record GetAdminLogsQuery(int Page, int PageSize, Guid? AdminId, string? Action, DateTime? From, DateTime? To, string? SortBy, string? SortDirection) : IRequest<AdminPagedResponse<AdminLogSummaryResponse>>;

public interface IAdminRepository
{
    Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminUserSummaryResponse>> GetUsersAsync(GetAdminUsersQuery query, CancellationToken cancellationToken);
    Task<AdminUserDetailResponse?> GetUserDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> UpdateUserStatusAsync(Guid adminId, Guid id, AccountStatus status, string? reason, CancellationToken cancellationToken);
    Task<bool> UpdateUserVerifyAsync(Guid adminId, Guid id, bool isVerified, CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminIdentitySummaryResponse>> GetIdentitiesAsync(GetAdminIdentitiesQuery query, CancellationToken cancellationToken);
    Task<AdminIdentityDetailResponse?> GetIdentityDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ApproveIdentityAsync(Guid adminId, Guid id, CancellationToken cancellationToken);
    Task<bool> RejectIdentityAsync(Guid adminId, Guid id, string? reason, CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminPostSummaryResponse>> GetPostsAsync(GetAdminPostsQuery query, PostType postType, CancellationToken cancellationToken);
    Task<AdminPostDetailResponse?> GetPostDetailAsync(Guid id, PostType? postType, CancellationToken cancellationToken);
    Task<bool> SetPostStatusAsync(Guid adminId, Guid id, PostStatus status, PostType? postType, string action, CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminReportSummaryResponse>> GetReportsAsync(GetAdminReportsQuery query, CancellationToken cancellationToken);
    Task<AdminReportDetailResponse?> GetReportDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ApproveReportAsync(Guid adminId, Guid id, string? action, CancellationToken cancellationToken);
    Task<bool> RejectReportAsync(Guid adminId, Guid id, CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminHashtagSummaryResponse>> GetHashtagsAsync(GetAdminHashtagsQuery query, CancellationToken cancellationToken);
    Task<bool?> RenameHashtagAsync(Guid adminId, Guid id, string name, CancellationToken cancellationToken);
    Task<bool> DeleteHashtagAsync(Guid adminId, Guid id, CancellationToken cancellationToken);
    Task CreateAnnouncementAsync(Guid adminId, string title, string content, string? imageUrl, string sendTo, CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminConversationSummaryResponse>> GetConversationsAsync(GetAdminConversationsQuery query, CancellationToken cancellationToken);
    Task<AdminConversationDetailResponse?> GetConversationDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<AdminPagedResponse<AdminLogSummaryResponse>> GetLogsAsync(GetAdminLogsQuery query, CancellationToken cancellationToken);
}

public sealed class AdminPagedQueryValidator : AbstractValidator<(int Page, int PageSize, string? SortDirection)>
{
    public AdminPagedQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortDirection).Must(x => string.IsNullOrWhiteSpace(x) || x.Equals("asc", StringComparison.OrdinalIgnoreCase) || x.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}
