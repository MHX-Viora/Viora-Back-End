using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController]
[Authorize]
[Route("api/developer/mini-apps")]
[Tags("Developer - Mini Apps")]
public sealed class DeveloperMiniAppsController(IDeveloperMiniAppService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MiniAppCredentialResponse>> Create(MiniAppConfigurationInput input, CancellationToken cancellationToken) =>
        Created(string.Empty, await service.CreateAsync(AccountId(), input, cancellationToken));
    [HttpGet]
    public Task<IReadOnlyList<MiniAppDeveloperView>> List(CancellationToken cancellationToken) => service.GetOwnedAsync(AccountId(), cancellationToken);
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
    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
}
