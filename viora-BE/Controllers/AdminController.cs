using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Admin;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "2")]
public sealed class AdminController(IMediator mediator) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> Dashboard(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetAdminDashboardQuery(), cancellationToken));

    [HttpGet("users")]
    public async Task<ActionResult<AdminPagedResponse<AdminUserSummaryResponse>>> Users(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] AccountStatus? status = null,
        [FromQuery] UserIdentityState? identityStatus = null,
        [FromQuery] bool? isVerified = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminUsersQuery(page, pageSize, keyword, status, identityStatus, isVerified, sortBy, sortDirection), cancellationToken));

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> UserDetail(Guid id, CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(new GetAdminUserDetailQuery(id), cancellationToken));

    [HttpPatch("users/{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(Guid id, UpdateAdminUserStatusRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new UpdateAdminUserStatusCommand(adminId, id, request.Status, request.Reason), cancellationToken));
    }

    [HttpPatch("users/{id:guid}/verify")]
    public async Task<IActionResult> UpdateUserVerify(Guid id, UpdateAdminUserVerifyRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new UpdateAdminUserVerifyCommand(adminId, id, request.IsVerified), cancellationToken));
    }

    [HttpGet("identities")]
    public async Task<ActionResult<AdminPagedResponse<AdminIdentitySummaryResponse>>> Identities(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] IdentitySubmissionStatus? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminIdentitiesQuery(page, pageSize, keyword, status, sortBy, sortDirection), cancellationToken));

    [HttpGet("identities/{id:guid}")]
    public async Task<IActionResult> IdentityDetail(Guid id, CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(new GetAdminIdentityDetailQuery(id), cancellationToken));

    [HttpPatch("identities/{id:guid}/approve")]
    public async Task<IActionResult> ApproveIdentity(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new ApproveAdminIdentityCommand(adminId, id), cancellationToken));
    }

    [HttpPatch("identities/{id:guid}/reject")]
    public async Task<IActionResult> RejectIdentity(Guid id, RejectAdminIdentityRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new RejectAdminIdentityCommand(adminId, id, request.Reason), cancellationToken));
    }

    [HttpGet("posts")]
    public async Task<ActionResult<AdminPagedResponse<AdminPostSummaryResponse>>> Posts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] bool? reported = null,
        [FromQuery] PostStatus? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminVideosQuery(page, pageSize, keyword, userId, reported, status, sortBy, sortDirection), cancellationToken));

    [HttpGet("posts/{id:guid}")]
    public async Task<IActionResult> PostDetail(Guid id, CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(new GetAdminPostDetailQuery(id, PostType.Post), cancellationToken));

    [HttpPatch("posts/{id:guid}/hide")]
    public async Task<IActionResult> HidePost(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new HideAdminPostCommand(adminId, id, PostType.Post), cancellationToken));
    }

    [HttpPatch("posts/{id:guid}/restore")]
    public async Task<IActionResult> RestorePost(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new RestoreAdminPostCommand(adminId, id, PostType.Post), cancellationToken));
    }

    [HttpDelete("posts/{id:guid}")]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new DeleteAdminPostCommand(adminId, id, PostType.Post), cancellationToken));
    }

    [HttpGet("videos")]
    public async Task<ActionResult<AdminPagedResponse<AdminPostSummaryResponse>>> Videos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] bool? reported = null,
        [FromQuery] PostStatus? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminPostsQuery(page, pageSize, keyword, userId, reported, status, sortBy, sortDirection), cancellationToken));

    [HttpGet("videos/{id:guid}")]
    public async Task<IActionResult> VideoDetail(Guid id, CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(new GetAdminPostDetailQuery(id, PostType.ShortVideo), cancellationToken));

    [HttpPatch("videos/{id:guid}/hide")]
    public async Task<IActionResult> HideVideo(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new HideAdminPostCommand(adminId, id, PostType.ShortVideo), cancellationToken));
    }

    [HttpPatch("videos/{id:guid}/restore")]
    public async Task<IActionResult> RestoreVideo(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new RestoreAdminPostCommand(adminId, id, PostType.ShortVideo), cancellationToken));
    }

    [HttpDelete("videos/{id:guid}")]
    public async Task<IActionResult> DeleteVideo(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new DeleteAdminPostCommand(adminId, id, PostType.ShortVideo), cancellationToken));
    }

    [HttpGet("reports")]
    public async Task<ActionResult<AdminPagedResponse<AdminReportSummaryResponse>>> Reports(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ReportStatus? status = null,
        [FromQuery] ReportTargetType? targetType = null,
        [FromQuery] ReportReason? reason = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminReportsQuery(page, pageSize, status, targetType, reason, sortBy, sortDirection), cancellationToken));

    [HttpGet("reports/{id:guid}")]
    public async Task<IActionResult> ReportDetail(Guid id, CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(new GetAdminReportDetailQuery(id), cancellationToken));

    [HttpPatch("reports/{id:guid}/approve")]
    public async Task<IActionResult> ApproveReport(Guid id, ApproveAdminReportRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new ApproveAdminReportCommand(adminId, id, request.Action), cancellationToken));
    }

    [HttpPatch("reports/{id:guid}/reject")]
    public async Task<IActionResult> RejectReport(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new RejectAdminReportCommand(adminId, id), cancellationToken));
    }

    [HttpGet("hashtags")]
    public async Task<ActionResult<AdminPagedResponse<AdminHashtagSummaryResponse>>> Hashtags(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminHashtagsQuery(page, pageSize, keyword, sortBy, sortDirection), cancellationToken));

    [HttpPatch("hashtags/{id:guid}")]
    public async Task<IActionResult> RenameHashtag(Guid id, RenameAdminHashtagRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        var result = await mediator.Send(new RenameAdminHashtagCommand(adminId, id, request.Name), cancellationToken);
        if (result is null) return NotFound();
        return result.Success ? Ok(result) : Conflict(result);
    }

    [HttpDelete("hashtags/{id:guid}")]
    public async Task<IActionResult> DeleteHashtag(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return ToResult(await mediator.Send(new DeleteAdminHashtagCommand(adminId, id), cancellationToken));
    }

    [HttpPost("announcements")]
    public async Task<ActionResult<AdminMutationResponse>> Announcement(CreateAdminAnnouncementRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        return Ok(await mediator.Send(new CreateAdminAnnouncementCommand(adminId, request.Title, request.Content, request.ImageUrl, request.SendTo), cancellationToken));
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<AdminPagedResponse<AdminConversationSummaryResponse>>> Conversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] ConversationType? type = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminConversationsQuery(page, pageSize, keyword, type, sortBy, sortDirection), cancellationToken));

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> ConversationDetail(Guid id, CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(new GetAdminConversationDetailQuery(id), cancellationToken));

    [HttpGet("logs")]
    public async Task<ActionResult<AdminPagedResponse<AdminLogSummaryResponse>>> Logs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? adminId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAdminLogsQuery(page, pageSize, adminId, action, from, to, sortBy, sortDirection), cancellationToken));

    private bool TryGetAdminId(out Guid userId) => Guid.TryParse(User.FindFirstValue("user_id"), out userId);

    private static IActionResult ToResult<T>(T? value) where T : class => value is null ? new NotFoundResult() : new OkObjectResult(value);
}

public sealed record UpdateAdminUserStatusRequest(AccountStatus Status, string? Reason);
public sealed record UpdateAdminUserVerifyRequest(bool IsVerified);
public sealed record RejectAdminIdentityRequest(string? Reason);
public sealed record ApproveAdminReportRequest(string? Action);
public sealed record RenameAdminHashtagRequest(string Name);
public sealed record CreateAdminAnnouncementRequest(string Title, string Content, string? ImageUrl, string SendTo);
