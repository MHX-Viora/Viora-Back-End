using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/mini-apps")]
[Tags("Admin - Mini Apps")]
public sealed class AdminMiniAppsController(IMiniAppManagementService service) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<MiniAppAdminDashboard> Dashboard(CancellationToken cancellationToken) => service.GetDashboardAsync(cancellationToken);

    [HttpGet]
    public Task<MiniAppAdminPage<MiniAppAdminListItem>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] MiniAppStatus? status = null, CancellationToken cancellationToken = default) =>
        service.GetAppsAsync(page, pageSize, search, status, cancellationToken);

    [HttpGet("permissions")]
    public Task<IReadOnlyList<MiniAppPermissionDto>> Permissions(CancellationToken cancellationToken) => service.GetPermissionsAsync(cancellationToken);

    [HttpGet("logs")]
    public Task<MiniAppAdminPage<MiniAppAuditDto>> Logs([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? miniAppId = null, CancellationToken cancellationToken = default) =>
        service.GetAuditLogsAsync(page, pageSize, miniAppId, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MiniAppDeveloperView>> Detail(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAppAsync(id, cancellationToken);
        return result is null ? NotFound(new MiniAppErrorResponse(MiniAppErrorCodes.NotFound, "Mini App không tồn tại.")) : Ok(result);
    }

    [HttpPut("{id:guid}")]
    public Task Update(Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken) => service.UpdateAppAsync(AccountId(), id, input, cancellationToken);

    [HttpPost("{id:guid}/approve")]
    public Task Approve(Guid id, CancellationToken cancellationToken) => service.SetAppStatusAsync(AccountId(), id, MiniAppStatus.Active, null, cancellationToken);
    [HttpPost("{id:guid}/reject")]
    public Task Reject(Guid id, StatusReasonRequest request, CancellationToken cancellationToken) => service.SetAppStatusAsync(AccountId(), id, MiniAppStatus.Rejected, request.Reason, cancellationToken);
    [HttpPost("{id:guid}/suspend")]
    public Task Suspend(Guid id, StatusReasonRequest request, CancellationToken cancellationToken) => service.SetAppStatusAsync(AccountId(), id, MiniAppStatus.Suspended, request.Reason, cancellationToken);
    [HttpPost("{id:guid}/reactivate")]
    public Task Reactivate(Guid id, CancellationToken cancellationToken) => service.SetAppStatusAsync(AccountId(), id, MiniAppStatus.Active, null, cancellationToken);

    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
}

public sealed record StatusReasonRequest(string? Reason);
