using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController]
[Authorize]
[Route("api/developer/mini-apps")]
[Tags("Developer - Mini Apps")]
public sealed class DeveloperMiniAppsController(IDeveloperMiniAppService service, IMiniAppManagementService management) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MiniAppCredentialResponse>> Create(MiniAppConfigurationInput input, CancellationToken cancellationToken) =>
        Created(string.Empty, await service.CreateAsync(AccountId(), input, cancellationToken));
    [HttpGet]
    public Task<IReadOnlyList<MiniAppDeveloperView>> List(CancellationToken cancellationToken) => service.GetOwnedAsync(AccountId(), cancellationToken);
    [HttpGet("permissions")]
    public Task<IReadOnlyList<MiniAppPermissionDto>> Permissions(CancellationToken ct) => management.GetPermissionsAsync(ct);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MiniAppDeveloperView>> Detail(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetOwnedAsync(AccountId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpPut("{id:guid}")]
    public Task Update(Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken) => service.UpdateOwnedAsync(AccountId(), id, input, cancellationToken);
    [HttpPost("{id:guid}/submit-review")]
    public Task Submit(Guid id, CancellationToken cancellationToken) => service.SubmitReviewAsync(AccountId(), id, cancellationToken);
    [HttpPost("{id:guid}/rotate-secret")]
    public Task<MiniAppCredentialResponse> Rotate(Guid id, CancellationToken cancellationToken) => service.RotateSecretAsync(AccountId(), id, cancellationToken);
    [HttpGet("{id:guid}/domains")]
    public Task<IReadOnlyList<MiniAppDomainDto>> Domains(Guid id, CancellationToken ct) => service.GetDomainsAsync(AccountId(), id, ct);
    [HttpPost("{id:guid}/domains")]
    public Task<MiniAppDomainDto> AddDomain(Guid id, MiniAppDomainInput input, CancellationToken ct) => service.AddDomainAsync(AccountId(), id, input, ct);
    [HttpPost("{id:guid}/domains/{domainId:guid}/verify")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mini-app-launch")]
    public Task<MiniAppDomainDto> VerifyDomain(Guid id, Guid domainId, CancellationToken ct) => service.VerifyDomainAsync(AccountId(), id, domainId, ct);
    [HttpGet("{id:guid}/versions")]
    public Task<IReadOnlyList<MiniAppVersionDto>> Versions(Guid id, CancellationToken ct) => service.GetOwnedVersionsAsync(AccountId(), id, ct);
    [HttpGet("{id:guid}/analytics")]
    public Task<MiniAppAnalyticsDto> Analytics(Guid id, CancellationToken ct) => service.GetAnalyticsAsync(AccountId(), id, ct);
    [HttpGet("{id:guid}/audit")]
    public Task<MiniAppAdminPage<MiniAppAuditDto>> Audit(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.GetOwnedAuditAsync(AccountId(), id, page, pageSize, ct);
    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
}
