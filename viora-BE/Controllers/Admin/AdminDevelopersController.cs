using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/developers")]
[Tags("Admin - Developers")]
public sealed class AdminDevelopersController(IMiniAppManagementService service) : ControllerBase
{
    [HttpGet]
    public Task<MiniAppAdminPage<DeveloperDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] DeveloperStatus? status = null, CancellationToken cancellationToken = default) =>
        service.GetDevelopersAsync(page, pageSize, search, status, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<DeveloperDto>> Create(DeveloperInput input, CancellationToken cancellationToken) =>
        Created(string.Empty, await service.CreateDeveloperAsync(AccountId(), input, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public Task Approve(Guid id, CancellationToken cancellationToken) => service.SetDeveloperStatusAsync(AccountId(), id, DeveloperStatus.Active, cancellationToken);
    [HttpPost("{id:guid}/suspend")]
    public Task Suspend(Guid id, CancellationToken cancellationToken) => service.SetDeveloperStatusAsync(AccountId(), id, DeveloperStatus.Suspended, cancellationToken);
    [HttpPost("{id:guid}/reject")]
    public Task Reject(Guid id, CancellationToken cancellationToken) => service.SetDeveloperStatusAsync(AccountId(), id, DeveloperStatus.Rejected, cancellationToken);

    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
}
