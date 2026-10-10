using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController, Authorize, Route("api/developer")]
public sealed class DeveloperProfileController(IDeveloperMiniAppService service) : ControllerBase
{
    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("profile")]
    public async Task<ActionResult<DeveloperProfileDto>> Profile(CancellationToken ct)
    {
        var result = await service.GetProfileAsync(AccountId(), ct); return result is null ? NotFound() : Ok(result);
    }
    [HttpPost("profile"), EnableRateLimiting("mini-app-launch")]
    public Task<DeveloperProfileDto> Register(DeveloperInput input, CancellationToken ct) => service.RegisterDeveloperAsync(AccountId(), input, ct);
    [HttpPut("profile")]
    public Task<DeveloperProfileDto> Update(DeveloperInput input, CancellationToken ct) => service.UpdateProfileAsync(AccountId(), input, ct);
    [HttpGet("team")]
    public Task<IReadOnlyList<DeveloperTeamDto>> Team(CancellationToken ct) => service.GetTeamAsync(AccountId(), ct);
    [HttpPost("team")]
    public Task<DeveloperTeamDto> AddMember(DeveloperTeamInput input, CancellationToken ct) => service.AddTeamAsync(AccountId(), input, ct);
    [HttpDelete("team/{id:guid}")]
    public Task RemoveMember(Guid id, CancellationToken ct) => service.RemoveTeamAsync(AccountId(), id, ct);
}
