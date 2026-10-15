using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Developer;

[ApiController, Authorize, Route("api/developer")]
public sealed class DeveloperMembershipController(IDeveloperMembershipService service) : ControllerBase
{
    private Guid AccountId => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("organizations")]
    public Task<IReadOnlyList<DeveloperOrganizationDto>> Organizations(CancellationToken ct) => service.GetOrganizationsAsync(AccountId, ct);
    [HttpGet("mini-apps/{appId:guid}/members")]
    public Task<IReadOnlyList<MiniAppMembershipDto>> Members(Guid appId, CancellationToken ct) => service.GetAppMembersAsync(AccountId, appId, ct);
    [HttpPost("mini-apps/{appId:guid}/invitations")]
    public Task<MiniAppMemberInvitationDto> Invite(Guid appId, MiniAppMemberInvitationInput input, CancellationToken ct) => service.InviteAppMemberAsync(AccountId, appId, input, ct);
    [HttpGet("mini-apps/{appId:guid}/invitations")]
    public Task<IReadOnlyList<MiniAppMemberInvitationDto>> Invitations(Guid appId, CancellationToken ct) => service.GetAppInvitationsAsync(AccountId, appId, ct);
    [HttpDelete("mini-apps/{appId:guid}/invitations/{invitationId:guid}")]
    public Task Cancel(Guid appId, Guid invitationId, CancellationToken ct) => service.CancelAppInvitationAsync(AccountId, appId, invitationId, ct);
    [HttpGet("invitations")]
    public Task<IReadOnlyList<MiniAppMemberInvitationDto>> MyInvitations(CancellationToken ct) => service.GetMyInvitationsAsync(AccountId, ct);
    [HttpPost("invitations/{id:guid}/accept")]
    public Task<MiniAppMembershipDto> Accept(Guid id, CancellationToken ct) => service.AcceptAppInvitationAsync(AccountId, id, ct);
    [HttpPut("mini-apps/{appId:guid}/members/{memberId:guid}")]
    public Task<MiniAppMembershipDto> Update(Guid appId, Guid memberId, DeveloperTeamRoleInput input, CancellationToken ct) => service.UpdateAppMemberAsync(AccountId, appId, memberId, input, ct);
    [HttpDelete("mini-apps/{appId:guid}/members/{memberId:guid}")]
    public Task Remove(Guid appId, Guid memberId, CancellationToken ct) => service.RemoveAppMemberAsync(AccountId, appId, memberId, ct);
}
