using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController, Authorize, Route("api/developer")]
public sealed class DeveloperProfileController(IDeveloperMiniAppService service, IDeveloperPlatformService platform, IDeveloperReauthenticationService reauthentication) : ControllerBase
{
    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("profile")]
    public async Task<ActionResult<DeveloperProfileDto>> Profile(CancellationToken ct)
    {
        var result = await platform.GetProfileAsync(AccountId(), ct); return result is null ? NotFound() : Ok(result);
    }
    [HttpPost("profile"), EnableRateLimiting("mini-app-launch")]
    public Task<DeveloperProfileDto> Register(DeveloperInput input, CancellationToken ct) => platform.SaveDraftAsync(AccountId(), input, ct);
    [HttpPut("profile")]
    public Task<DeveloperProfileDto> Update(DeveloperInput input, CancellationToken ct) => platform.SaveDraftAsync(AccountId(), input, ct);
    [HttpGet("terms")]
    [AllowAnonymous]
    public DeveloperTermsDto Terms() => platform.GetTerms();
    [HttpPost("application/draft"), EnableRateLimiting("mini-app-launch")]
    public Task<DeveloperProfileDto> Draft(DeveloperInput input, CancellationToken ct) => platform.SaveDraftAsync(AccountId(), input, ct);
    [HttpPost("application/submit"), EnableRateLimiting("mini-app-launch")]
    public Task<DeveloperProfileDto> Submit(DeveloperSubmitInput input, CancellationToken ct) => platform.SubmitAsync(AccountId(), input, ct);
    [HttpPost("email-verification/request"), EnableRateLimiting("mini-app-launch")]
    public Task<DeveloperEmailChallenge> RequestEmail(DeveloperEmailRequest input, CancellationToken ct) => platform.RequestEmailAsync(AccountId(), input, ct);
    [HttpPost("email-verification/confirm"), EnableRateLimiting("mini-app-launch")]
    public Task<DeveloperEmailConfirmed> ConfirmEmail(DeveloperEmailConfirm input, CancellationToken ct) => platform.ConfirmEmailAsync(AccountId(), input, ct);
    [HttpGet("team")]
    public Task<IReadOnlyList<DeveloperTeamDto>> Team(CancellationToken ct) => service.GetTeamAsync(AccountId(), ct);
    [HttpPost("team")]
    public async Task<DeveloperTeamDto> AddMember(DeveloperTeamInput input, CancellationToken ct)
    {
        await reauthentication.ConsumeAsync(AccountId(), "ManageTeam", null, Request.Headers["X-Developer-Reauth"].FirstOrDefault(), ct);
        return await service.AddTeamAsync(AccountId(), input, ct);
    }
    [HttpDelete("team/{id:guid}")]
    public async Task RemoveMember(Guid id, CancellationToken ct)
    {
        await reauthentication.ConsumeAsync(AccountId(), "ManageTeam", null, Request.Headers["X-Developer-Reauth"].FirstOrDefault(), ct);
        await service.RemoveTeamAsync(AccountId(), id, ct);
    }
    [HttpPut("team/{id:guid}")]
    public async Task<DeveloperTeamDto> UpdateMemberRole(Guid id, DeveloperTeamRoleInput input, CancellationToken ct)
    {
        await reauthentication.ConsumeAsync(AccountId(), "ManageTeam", null, Request.Headers["X-Developer-Reauth"].FirstOrDefault(), ct);
        return await service.UpdateTeamRoleAsync(AccountId(), id, input, ct);
    }
}
