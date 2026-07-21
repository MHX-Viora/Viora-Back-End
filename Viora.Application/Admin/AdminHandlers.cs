using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Admin;

public sealed class GetAdminDashboardHandler(IAdminRepository repository) : IRequestHandler<GetAdminDashboardQuery, AdminDashboardResponse>
{
    public Task<AdminDashboardResponse> Handle(GetAdminDashboardQuery request, CancellationToken cancellationToken) => repository.GetDashboardAsync(cancellationToken);
}

public sealed class GetAdminUsersHandler(IAdminRepository repository) : IRequestHandler<GetAdminUsersQuery, AdminPagedResponse<AdminUserSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminUserSummaryResponse>> Handle(GetAdminUsersQuery request, CancellationToken cancellationToken) => repository.GetUsersAsync(request, cancellationToken);
}

public sealed class GetAdminUserDetailHandler(IAdminRepository repository) : IRequestHandler<GetAdminUserDetailQuery, AdminUserDetailResponse?>
{
    public Task<AdminUserDetailResponse?> Handle(GetAdminUserDetailQuery request, CancellationToken cancellationToken) => repository.GetUserDetailAsync(request.Id, cancellationToken);
}

public sealed class UpdateAdminUserStatusHandler(IAdminRepository repository) : IRequestHandler<UpdateAdminUserStatusCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(UpdateAdminUserStatusCommand request, CancellationToken cancellationToken) =>
        await repository.UpdateUserStatusAsync(request.AdminId, request.Id, request.Status, request.Reason, cancellationToken)
            ? new AdminMutationResponse(true, "User status updated.")
            : null;
}

public sealed class UpdateAdminUserVerifyHandler(IAdminRepository repository) : IRequestHandler<UpdateAdminUserVerifyCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(UpdateAdminUserVerifyCommand request, CancellationToken cancellationToken) =>
        await repository.UpdateUserVerifyAsync(request.AdminId, request.Id, request.IsVerified, cancellationToken)
            ? new AdminMutationResponse(true, "User verification updated.")
            : null;
}

public sealed class GetAdminIdentitiesHandler(IAdminRepository repository) : IRequestHandler<GetAdminIdentitiesQuery, AdminPagedResponse<AdminIdentitySummaryResponse>>
{
    public Task<AdminPagedResponse<AdminIdentitySummaryResponse>> Handle(GetAdminIdentitiesQuery request, CancellationToken cancellationToken) => repository.GetIdentitiesAsync(request, cancellationToken);
}

public sealed class GetAdminIdentityDetailHandler(IAdminRepository repository) : IRequestHandler<GetAdminIdentityDetailQuery, AdminIdentityDetailResponse?>
{
    public Task<AdminIdentityDetailResponse?> Handle(GetAdminIdentityDetailQuery request, CancellationToken cancellationToken) => repository.GetIdentityDetailAsync(request.Id, cancellationToken);
}

public sealed class ApproveAdminIdentityHandler(IAdminRepository repository) : IRequestHandler<ApproveAdminIdentityCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(ApproveAdminIdentityCommand request, CancellationToken cancellationToken) =>
        await repository.ApproveIdentityAsync(request.AdminId, request.Id, cancellationToken)
            ? new AdminMutationResponse(true, "Identity approved.")
            : null;
}

public sealed class RejectAdminIdentityHandler(IAdminRepository repository) : IRequestHandler<RejectAdminIdentityCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(RejectAdminIdentityCommand request, CancellationToken cancellationToken) =>
        await repository.RejectIdentityAsync(request.AdminId, request.Id, request.Reason, cancellationToken)
            ? new AdminMutationResponse(true, "Identity rejected.")
            : null;
}

public sealed class GetAdminPostsHandler(IAdminRepository repository) : IRequestHandler<GetAdminPostsQuery, AdminPagedResponse<AdminPostSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminPostSummaryResponse>> Handle(GetAdminPostsQuery request, CancellationToken cancellationToken) => repository.GetPostsAsync(request, PostType.Post, cancellationToken);
}

public sealed class GetAdminVideosHandler(IAdminRepository repository) : IRequestHandler<GetAdminVideosQuery, AdminPagedResponse<AdminPostSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminPostSummaryResponse>> Handle(GetAdminVideosQuery request, CancellationToken cancellationToken) =>
        repository.GetPostsAsync(new GetAdminPostsQuery(request.Page, request.PageSize, request.Keyword, request.UserId, request.Reported, request.Status, request.SortBy, request.SortDirection), PostType.ShortVideo, cancellationToken);
}

public sealed class GetAdminPostDetailHandler(IAdminRepository repository) : IRequestHandler<GetAdminPostDetailQuery, AdminPostDetailResponse?>
{
    public Task<AdminPostDetailResponse?> Handle(GetAdminPostDetailQuery request, CancellationToken cancellationToken) => repository.GetPostDetailAsync(request.Id, request.PostType, cancellationToken);
}

public sealed class HideAdminPostHandler(IAdminRepository repository) : IRequestHandler<HideAdminPostCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(HideAdminPostCommand request, CancellationToken cancellationToken) =>
        await repository.SetPostStatusAsync(request.AdminId, request.Id, PostStatus.Hidden, request.PostType, "HidePost", cancellationToken)
            ? new AdminMutationResponse(true, "Post hidden.")
            : null;
}

public sealed class RestoreAdminPostHandler(IAdminRepository repository) : IRequestHandler<RestoreAdminPostCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(RestoreAdminPostCommand request, CancellationToken cancellationToken) =>
        await repository.SetPostStatusAsync(request.AdminId, request.Id, PostStatus.Published, request.PostType, "RestorePost", cancellationToken)
            ? new AdminMutationResponse(true, "Post restored.")
            : null;
}

public sealed class DeleteAdminPostHandler(IAdminRepository repository) : IRequestHandler<DeleteAdminPostCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(DeleteAdminPostCommand request, CancellationToken cancellationToken) =>
        await repository.SetPostStatusAsync(request.AdminId, request.Id, PostStatus.Deleted, request.PostType, "DeletePost", cancellationToken)
            ? new AdminMutationResponse(true, "Post deleted.")
            : null;
}

public sealed class GetAdminReportsHandler(IAdminRepository repository) : IRequestHandler<GetAdminReportsQuery, AdminPagedResponse<AdminReportSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminReportSummaryResponse>> Handle(GetAdminReportsQuery request, CancellationToken cancellationToken) => repository.GetReportsAsync(request, cancellationToken);
}

public sealed class GetAdminReportDetailHandler(IAdminRepository repository) : IRequestHandler<GetAdminReportDetailQuery, AdminReportDetailResponse?>
{
    public Task<AdminReportDetailResponse?> Handle(GetAdminReportDetailQuery request, CancellationToken cancellationToken) => repository.GetReportDetailAsync(request.Id, cancellationToken);
}

public sealed class ApproveAdminReportHandler(IAdminRepository repository) : IRequestHandler<ApproveAdminReportCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(ApproveAdminReportCommand request, CancellationToken cancellationToken) =>
        await repository.ApproveReportAsync(request.AdminId, request.Id, request.Action, cancellationToken)
            ? new AdminMutationResponse(true, "Report approved.")
            : null;
}

public sealed class RejectAdminReportHandler(IAdminRepository repository) : IRequestHandler<RejectAdminReportCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(RejectAdminReportCommand request, CancellationToken cancellationToken) =>
        await repository.RejectReportAsync(request.AdminId, request.Id, cancellationToken)
            ? new AdminMutationResponse(true, "Report rejected.")
            : null;
}

public sealed class GetAdminHashtagsHandler(IAdminRepository repository) : IRequestHandler<GetAdminHashtagsQuery, AdminPagedResponse<AdminHashtagSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminHashtagSummaryResponse>> Handle(GetAdminHashtagsQuery request, CancellationToken cancellationToken) => repository.GetHashtagsAsync(request, cancellationToken);
}

public sealed class RenameAdminHashtagHandler(IAdminRepository repository) : IRequestHandler<RenameAdminHashtagCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(RenameAdminHashtagCommand request, CancellationToken cancellationToken)
    {
        var result = await repository.RenameHashtagAsync(request.AdminId, request.Id, request.Name, cancellationToken);
        return result is null ? null : new AdminMutationResponse(result.Value, result.Value ? "Hashtag renamed." : "Hashtag name already exists.");
    }
}

public sealed class DeleteAdminHashtagHandler(IAdminRepository repository) : IRequestHandler<DeleteAdminHashtagCommand, AdminMutationResponse?>
{
    public async Task<AdminMutationResponse?> Handle(DeleteAdminHashtagCommand request, CancellationToken cancellationToken) =>
        await repository.DeleteHashtagAsync(request.AdminId, request.Id, cancellationToken)
            ? new AdminMutationResponse(true, "Hashtag deleted.")
            : null;
}

public sealed class CreateAdminAnnouncementHandler(IAdminRepository repository) : IRequestHandler<CreateAdminAnnouncementCommand, AdminMutationResponse>
{
    public async Task<AdminMutationResponse> Handle(CreateAdminAnnouncementCommand request, CancellationToken cancellationToken)
    {
        await repository.CreateAnnouncementAsync(request.AdminId, request.Title, request.Content, request.ImageUrl, request.SendTo, cancellationToken);
        return new AdminMutationResponse(true, "Announcement created.");
    }
}

public sealed class GetAdminConversationsHandler(IAdminRepository repository) : IRequestHandler<GetAdminConversationsQuery, AdminPagedResponse<AdminConversationSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminConversationSummaryResponse>> Handle(GetAdminConversationsQuery request, CancellationToken cancellationToken) => repository.GetConversationsAsync(request, cancellationToken);
}

public sealed class GetAdminConversationDetailHandler(IAdminRepository repository) : IRequestHandler<GetAdminConversationDetailQuery, AdminConversationDetailResponse?>
{
    public Task<AdminConversationDetailResponse?> Handle(GetAdminConversationDetailQuery request, CancellationToken cancellationToken) => repository.GetConversationDetailAsync(request.Id, cancellationToken);
}

public sealed class GetAdminLogsHandler(IAdminRepository repository) : IRequestHandler<GetAdminLogsQuery, AdminPagedResponse<AdminLogSummaryResponse>>
{
    public Task<AdminPagedResponse<AdminLogSummaryResponse>> Handle(GetAdminLogsQuery request, CancellationToken cancellationToken) => repository.GetLogsAsync(request, cancellationToken);
}
