namespace Viora.Application.MiniApps;

// Omitted context preserves legacy callers; clients should select an organization explicitly.
public interface IDeveloperOrganizationContext { Guid? OrganizationId { get; } }
public sealed record DeveloperOrganizationDto(Guid Id, string Name, string Status, string Role, bool IsAccountOwner);
public sealed record MiniAppMembershipDto(Guid Id, Guid AccountId, string Role, DateTime CreatedAt);
public sealed record MiniAppMemberInvitationInput(Guid AccountId, string Role = "Developer");
public sealed record MiniAppMemberInvitationDto(Guid Id, Guid MiniAppId, Guid AccountId, string Role, DateTime ExpiresAt, DateTime? AcceptedAt);
public interface IDeveloperMembershipService
{
    Task<IReadOnlyList<DeveloperOrganizationDto>> GetOrganizationsAsync(Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<MiniAppMembershipDto>> GetAppMembersAsync(Guid accountId, Guid appId, CancellationToken ct);
    Task<MiniAppMemberInvitationDto> InviteAppMemberAsync(Guid accountId, Guid appId, MiniAppMemberInvitationInput input, CancellationToken ct);
    Task<MiniAppMembershipDto> AcceptAppInvitationAsync(Guid accountId, Guid invitationId, CancellationToken ct);
    Task<IReadOnlyList<MiniAppMemberInvitationDto>> GetAppInvitationsAsync(Guid accountId, Guid appId, CancellationToken ct);
    Task<IReadOnlyList<MiniAppMemberInvitationDto>> GetMyInvitationsAsync(Guid accountId, CancellationToken ct);
    Task CancelAppInvitationAsync(Guid accountId, Guid appId, Guid invitationId, CancellationToken ct);
    Task<MiniAppMembershipDto> UpdateAppMemberAsync(Guid accountId, Guid appId, Guid memberId, DeveloperTeamRoleInput input, CancellationToken ct);
    Task RemoveAppMemberAsync(Guid accountId, Guid appId, Guid memberId, CancellationToken ct);
}
