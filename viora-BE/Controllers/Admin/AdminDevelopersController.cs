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
public sealed class AdminDevelopersController(IMiniAppManagementService service, IDeveloperPlatformService platform) : ControllerBase
{
    [HttpGet]
    public Task<MiniAppAdminPage<DeveloperDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] DeveloperStatus? status = null, CancellationToken cancellationToken = default) =>
        service.GetDevelopersAsync(page, pageSize, search, status, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<DeveloperDto>> Create(DeveloperInput input, CancellationToken cancellationToken) =>
        Created(string.Empty, await service.CreateDeveloperAsync(AccountId(), input, cancellationToken));

    [HttpGet("{id:guid}/application")]
    public Task<DeveloperProfileDto> Application(Guid id, CancellationToken ct) => platform.GetAdminApplicationAsync(id, ct);
    [HttpPost("{id:guid}/review")]
    public Task<DeveloperProfileDto> Review(Guid id, DeveloperReviewInput input, CancellationToken ct) => platform.ReviewAsync(AccountId(), id, input, ct);
    [HttpPost("{id:guid}/approve")]
    public Task<DeveloperProfileDto> Approve(Guid id, DeveloperReviewInput input, CancellationToken ct) => platform.ReviewAsync(AccountId(), id, input with { Status = "Approved" }, ct);
    [HttpPost("{id:guid}/suspend")]
    public Task<DeveloperProfileDto> Suspend(Guid id, DeveloperReviewInput input, CancellationToken ct) => platform.ReviewAsync(AccountId(), id, input with { Status = "Suspended" }, ct);
    [HttpPost("{id:guid}/reject")]
    public Task<DeveloperProfileDto> Reject(Guid id, DeveloperReviewInput input, CancellationToken ct) => platform.ReviewAsync(AccountId(), id, input with { Status = "Rejected" }, ct);

    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
}
