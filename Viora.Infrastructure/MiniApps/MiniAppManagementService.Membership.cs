using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService
{
    private async Task<DeveloperMembershipRole> AppRoleFor(Guid accountId, MiniApp app, CancellationToken ct)
    {
        if (app.Developer.AccountId == accountId) return DeveloperMembershipRole.Owner;
        var scoped = await db.Set<MiniAppMembership>().Where(m => m.MiniAppId == app.Id && m.AccountId == accountId).Select(m => (DeveloperMembershipRole?)m.Role).SingleOrDefaultAsync(ct);
        return scoped ?? await RoleFor(accountId, app.Developer, ct);
    }

    private async Task RequireAppCapability(Guid accountId, MiniApp app, string capability, CancellationToken ct)
    {
        await RequireActiveAccount(accountId, ct);
        if (capability is not ("read" or "analytics") && (app.Developer.Status != DeveloperStatus.Active || app.DeletedAt != null || app.Status is MiniAppStatus.Suspended or MiniAppStatus.Archived or MiniAppStatus.Deleted))
            throw new MiniAppException("MINI_APP_RESTRICTED", "App or organization is restricted; changes are not permitted.", 403);
        var role = await AppRoleFor(accountId, app, ct);
        var allowed = capability switch
        {
            "credentials" or "ownership" or "delete" => role == DeveloperMembershipRole.Owner,
            "manage" => role is DeveloperMembershipRole.Owner or DeveloperMembershipRole.Admin,
            "edit" => role is DeveloperMembershipRole.Owner or DeveloperMembershipRole.Admin or DeveloperMembershipRole.Developer or DeveloperMembershipRole.Member,
            "analytics" => true,
            "audit" => role is DeveloperMembershipRole.Owner or DeveloperMembershipRole.Admin,
            "read" => true,
            _ => false
        };
        if (!allowed) throw new MiniAppException("PERMISSION_DENIED", "Current app role cannot perform this operation.", 403);
    }

    public async Task<IReadOnlyList<DeveloperOrganizationDto>> GetOrganizationsAsync(Guid accountId, CancellationToken ct)
    {
        await RequireActiveAccount(accountId, ct);
        var organizations = await db.Developers.AsNoTracking().Where(d => d.AccountId == accountId || db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId)).OrderBy(d => d.Name).ToListAsync(ct);
        var result = new List<DeveloperOrganizationDto>();
        foreach (var d in organizations) result.Add(new(d.Id, d.Name, d.Status.ToString(), (await RoleFor(accountId, d, ct)).ToString(), d.AccountId == accountId));
        return result;
    }

    public async Task<IReadOnlyList<MiniAppMembershipDto>> GetAppMembersAsync(Guid accountId, Guid appId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, appId, ct);
        var explicitMembers = await db.Set<MiniAppMembership>().AsNoTracking().Where(m => m.MiniAppId == appId).Select(m => new MiniAppMembershipDto(m.Id, m.AccountId, m.Role.ToString(), m.CreatedAt)).ToListAsync(ct);
        var organizationMembers = await db.DeveloperMemberships.AsNoTracking().Where(m => m.DeveloperId == app.DeveloperId).Select(m => new MiniAppMembershipDto(m.Id, m.AccountId, m.Role.ToString(), m.CreatedAt)).ToListAsync(ct);
        foreach (var member in organizationMembers) if (!explicitMembers.Any(m => m.AccountId == member.AccountId)) explicitMembers.Add(member);
        if (app.Developer.AccountId is Guid owner)
        {
            explicitMembers.RemoveAll(m => m.AccountId == owner);
            explicitMembers.Insert(0, new(app.DeveloperId, owner, "Owner", app.Developer.CreatedAt));
        }
        return explicitMembers;
    }

    private static DeveloperMembershipRole MemberRole(string value)
    {
        if (!Enum.TryParse<DeveloperMembershipRole>(value, out var role) || role is not (DeveloperMembershipRole.Admin or DeveloperMembershipRole.Developer or DeveloperMembershipRole.Viewer))
            throw new MiniAppException("VALIDATION_ERROR", "Choose Admin, Developer or Viewer; ownership cannot be assigned by this action.", 422);
        return role;
    }
    private async Task CanManageTarget(Guid actor, MiniApp app, Guid target, DeveloperMembershipRole role, CancellationToken ct)
    {
        await RequireAppCapability(actor, app, "manage", ct);
        if (target == actor || target == app.Developer.AccountId) throw new MiniAppException("OWNER_REQUIRED", "Cannot change yourself or the organization owner.", 403);
        if (role == DeveloperMembershipRole.Admin && await AppRoleFor(actor, app, ct) != DeveloperMembershipRole.Owner)
            throw new MiniAppException("OWNER_REQUIRED", "Only an owner may manage Admin roles.", 403);
    }
    private static MiniAppMembershipDto Member(MiniAppMembership m) => new(m.Id, m.AccountId, m.Role.ToString(), m.CreatedAt);
    private static MiniAppMemberInvitationDto Invitation(MiniAppMemberInvitation i) => new(i.Id, i.MiniAppId, i.AccountId, i.Role.ToString(), i.ExpiresAt, i.AcceptedAt);

    public async Task<MiniAppMemberInvitationDto> InviteAppMemberAsync(Guid accountId, Guid appId, MiniAppMemberInvitationInput input, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, appId, ct); var role = MemberRole(input.Role);
        await CanManageTarget(accountId, app, input.AccountId, role, ct);
        await RequireActiveAccount(input.AccountId, ct);
        if (await db.Set<MiniAppMembership>().AnyAsync(m => m.MiniAppId == appId && m.AccountId == input.AccountId, ct)) throw new MiniAppException("MEMBERSHIP_EXISTS", "App member already exists.", 409);
        var pending = await db.Set<MiniAppMemberInvitation>().Where(i => i.MiniAppId == appId && i.AccountId == input.AccountId && i.AcceptedAt == null && i.CancelledAt == null).ToListAsync(ct);
        foreach (var previous in pending) previous.CancelledAt = DateTime.UtcNow;
        var invitation = new MiniAppMemberInvitation { MiniAppId = appId, AccountId = input.AccountId, InvitedByAccountId = accountId, Role = role, ExpiresAt = DateTime.UtcNow.AddDays(7) };
        db.Set<MiniAppMemberInvitation>().Add(invitation); Audit(accountId, app.DeveloperId, appId, "AppMemberInvited", role.ToString()); await SaveAsync(ct);
        return Invitation(invitation);
    }

    public async Task<MiniAppMembershipDto> AcceptAppInvitationAsync(Guid accountId, Guid invitationId, CancellationToken ct)
    {
        await RequireActiveAccount(accountId, ct);
        var invitation = await db.Set<MiniAppMemberInvitation>().Include(i => i.MiniApp).ThenInclude(a => a.Developer).SingleOrDefaultAsync(i => i.Id == invitationId && i.AccountId == accountId, ct)
            ?? throw new MiniAppException("INVITATION_NOT_FOUND", "Invitation unavailable for this account.", 404);
        if (invitation.AcceptedAt != null || invitation.CancelledAt != null || invitation.ExpiresAt <= DateTime.UtcNow || invitation.MiniApp.DeletedAt != null)
            throw new MiniAppException("INVITATION_UNAVAILABLE", "Invitation was accepted, cancelled or expired.", 409);
        // Permission is checked again so a removed inviter cannot leave privileged grants behind.
        await CanManageTarget(invitation.InvitedByAccountId, invitation.MiniApp, accountId, invitation.Role, ct);
        if (await db.Set<MiniAppMembership>().AnyAsync(m => m.MiniAppId == invitation.MiniAppId && m.AccountId == accountId, ct)) throw new MiniAppException("MEMBERSHIP_EXISTS", "App member already exists.", 409);
        var member = new MiniAppMembership { MiniAppId = invitation.MiniAppId, AccountId = accountId, Role = invitation.Role };
        db.Set<MiniAppMembership>().Add(member); invitation.AcceptedAt = DateTime.UtcNow;
        Audit(accountId, invitation.MiniApp.DeveloperId, invitation.MiniAppId, "AppInvitationAccepted"); await SaveAsync(ct); return Member(member);
    }

    public async Task<IReadOnlyList<MiniAppMemberInvitationDto>> GetAppInvitationsAsync(Guid accountId, Guid appId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, appId, ct); await RequireAppCapability(accountId, app, "manage", ct);
        return (await db.Set<MiniAppMemberInvitation>().AsNoTracking().Where(i => i.MiniAppId == appId && i.CancelledAt == null && i.AcceptedAt == null && i.ExpiresAt > DateTime.UtcNow).ToListAsync(ct)).Select(Invitation).ToArray();
    }
    public async Task<IReadOnlyList<MiniAppMemberInvitationDto>> GetMyInvitationsAsync(Guid accountId, CancellationToken ct)
    {
        await RequireActiveAccount(accountId, ct);
        return (await db.Set<MiniAppMemberInvitation>().AsNoTracking().Where(i => i.AccountId == accountId && i.CancelledAt == null && i.AcceptedAt == null && i.ExpiresAt > DateTime.UtcNow && i.MiniApp.DeletedAt == null).ToListAsync(ct)).Select(Invitation).ToArray();
    }
    public async Task CancelAppInvitationAsync(Guid accountId, Guid appId, Guid invitationId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, appId, ct); await RequireAppCapability(accountId, app, "manage", ct);
        var invitation = await db.Set<MiniAppMemberInvitation>().SingleOrDefaultAsync(i => i.Id == invitationId && i.MiniAppId == appId && i.AcceptedAt == null, ct) ?? throw new MiniAppException("INVITATION_NOT_FOUND", "Invitation unavailable.", 404);
        await CanManageTarget(accountId, app, invitation.AccountId, invitation.Role, ct);
        invitation.CancelledAt = DateTime.UtcNow; Audit(accountId, app.DeveloperId, appId, "AppInvitationCancelled"); await SaveAsync(ct);
    }

    public async Task<MiniAppMembershipDto> UpdateAppMemberAsync(Guid accountId, Guid appId, Guid memberId, DeveloperTeamRoleInput input, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, appId, ct); var role = MemberRole(input.Role);
        var member = await db.Set<MiniAppMembership>().SingleOrDefaultAsync(m => m.Id == memberId && m.MiniAppId == appId, ct) ?? throw new MiniAppException("MEMBERSHIP_NOT_FOUND", "Only explicit app memberships can be edited here; organization roles use the organization team.", 404);
        await CanManageTarget(accountId, app, member.AccountId, role, ct); await CanManageTarget(accountId, app, member.AccountId, member.Role, ct);
        if (member.Role == DeveloperMembershipRole.Owner) throw new MiniAppException("OWNER_REQUIRED", "Ownership cannot be changed by role editing.", 403);
        member.Role = role; Audit(accountId, app.DeveloperId, appId, "AppMemberRoleChanged", role.ToString()); await SaveAsync(ct); return Member(member);
    }
    public async Task RemoveAppMemberAsync(Guid accountId, Guid appId, Guid memberId, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, appId, ct);
        var member = await db.Set<MiniAppMembership>().SingleOrDefaultAsync(m => m.Id == memberId && m.MiniAppId == appId, ct) ?? throw new MiniAppException("MEMBERSHIP_NOT_FOUND", "Only explicit app memberships can be removed here.", 404);
        await CanManageTarget(accountId, app, member.AccountId, member.Role, ct);
        if (member.Role == DeveloperMembershipRole.Owner) throw new MiniAppException("OWNER_REQUIRED", "App owner cannot be removed.", 403);
        db.Set<MiniAppMembership>().Remove(member); Audit(accountId, app.DeveloperId, appId, "AppMemberRemoved"); await SaveAsync(ct);
    }
}
