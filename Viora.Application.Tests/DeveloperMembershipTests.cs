using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;
using Fixture = global::MiniAppHybridIntegrationTests.Fixture;

namespace Viora.Application.Tests;

public sealed class DeveloperMembershipTests
{
    private sealed class Context(Guid id) : IDeveloperOrganizationContext { public Guid? OrganizationId => id; }

    [Fact]
    public async Task MultipleOrganizationsSelectExplicitContextAndCannotAccessOtherOrganization()
    {
        await using var f = await Fixture.Create(true);
        var first = await f.Db.Developers.SingleAsync();
        var second = new Developer { Name = "Second", Email = "second@example.com", Status = DeveloperStatus.Active };
        f.Db.Developers.Add(second);
        f.Db.DeveloperMemberships.Add(new() { DeveloperId = second.Id, AccountId = f.Owner.Id, Role = DeveloperMembershipRole.Developer });
        await f.Db.SaveChangesAsync();
        var legacy = await f.Management.GetProfileAsync(f.Owner.Id, default);
        Assert.Equal(first.Id, legacy!.Id);
        Assert.Equal(2, (await f.Management.GetOrganizationsAsync(f.Owner.Id, default)).Count);
        var selected = new MiniAppManagementService(f.Db, new ClientCredentialService(), organizationContext: new Context(second.Id));
        Assert.Equal(second.Id, (await selected.GetProfileAsync(f.Owner.Id, default))!.Id);
        var created = await selected.CreateAsync(f.Owner.Id, f.Input() with { Slug = "second-app" }, default);
        Assert.Equal(second.Id, (await f.Db.MiniApps.SingleAsync(a => a.Id == created.Id)).DeveloperId);
        var denied = new MiniAppManagementService(f.Db, new ClientCredentialService(), organizationContext: new Context(first.Id));
        Assert.Equal("ORGANIZATION_ACCESS_DENIED", (await Assert.ThrowsAsync<MiniAppException>(() => denied.GetProfileAsync(f.Other.Id, default))).Code);
    }

    [Fact]
    public async Task TeamMemberCanJoinMultipleOrganizationsWithoutGlobalMembershipConflict()
    {
        await using var f = await Fixture.Create(true);
        var otherOrganization = new Developer { Name = "Another", Email = "another@example.com", Status = DeveloperStatus.Active };
        f.Db.Developers.Add(otherOrganization);
        f.Db.DeveloperMemberships.Add(new() { DeveloperId = otherOrganization.Id, AccountId = f.Other.Id, Role = DeveloperMembershipRole.Viewer });
        await f.Db.SaveChangesAsync();
        await f.Management.AddTeamAsync(f.Owner.Id, new(f.Other.Id, "Developer"), default);
        Assert.Equal(2, (await f.Management.GetOrganizationsAsync(f.Other.Id, default)).Count);
        Assert.NotNull(await f.Management.GetProfileAsync(f.Other.Id, default));
    }

    [Theory]
    [InlineData("Viewer", false)]
    [InlineData("Developer", true)]
    [InlineData("Admin", true)]
    public async Task AcceptedInvitationGrantsOnlyAppScopeAndEnforcesRole(string role, bool canEdit)
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var another = await f.Management.CreateAsync(f.Owner.Id, f.Input() with { Slug = "another" }, default);
        var invitation = await f.Management.InviteAppMemberAsync(f.Owner.Id, app.Id, new(f.Other.Id, role), default);
        Assert.Null(await f.Management.GetOwnedAsync(f.Other.Id, app.Id, default));
        Assert.Equal("INVITATION_NOT_FOUND", (await Assert.ThrowsAsync<MiniAppException>(() => f.Management.AcceptAppInvitationAsync(f.Owner.Id, invitation.Id, default))).Code);
        var membership = await f.Management.AcceptAppInvitationAsync(f.Other.Id, invitation.Id, default);
        Assert.Equal(role, membership.Role);
        Assert.NotNull(await f.Management.GetOwnedAsync(f.Other.Id, app.Id, default));
        Assert.Null(await f.Management.GetOwnedAsync(f.Other.Id, another.Id, default));
        Assert.Single(await f.Management.GetOwnedAsync(f.Other.Id, default));
        Assert.NotNull(await f.Management.GetAnalyticsAsync(f.Other.Id, app.Id, default));
        if (role == "Viewer") await Assert.ThrowsAsync<MiniAppException>(() => f.Management.GetOwnedAuditAsync(f.Other.Id, app.Id, 1, 20, default));
        if (canEdit) await f.Management.UpdateOwnedAsync(f.Other.Id, app.Id, f.Input() with { Name = "Updated" }, default);
        else await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateOwnedAsync(f.Other.Id, app.Id, f.Input(), default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RotateSecretAsync(f.Other.Id, app.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.AcceptAppInvitationAsync(f.Other.Id, invitation.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateAppMemberAsync(f.Other.Id, app.Id, membership.Id, new("Admin"), default));
    }

    [Fact]
    public async Task InvitationsExpireAndCancelWithoutGrantingAccessAndOwnersCannotBeRemoved()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var invitation = await f.Management.InviteAppMemberAsync(f.Owner.Id, app.Id, new(f.Other.Id), default);
        await f.Management.CancelAppInvitationAsync(f.Owner.Id, app.Id, invitation.Id, default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.AcceptAppInvitationAsync(f.Other.Id, invitation.Id, default));
        invitation = await f.Management.InviteAppMemberAsync(f.Owner.Id, app.Id, new(f.Other.Id), default);
        var entity = await f.Db.MiniAppMemberInvitations.SingleAsync(i => i.Id == invitation.Id);
        entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.AcceptAppInvitationAsync(f.Other.Id, invitation.Id, default));
        var owner = await f.Db.MiniAppMemberships.SingleAsync(m => m.MiniAppId == app.Id && m.AccountId == f.Owner.Id);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RemoveAppMemberAsync(f.Owner.Id, app.Id, owner.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateAppMemberAsync(f.Owner.Id, app.Id, owner.Id, new("Viewer"), default));
    }

    [Fact]
    public async Task SuspendedOrganizationStopsMemberWritesButKeepsReadAccess()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var invitation = await f.Management.InviteAppMemberAsync(f.Owner.Id, app.Id, new(f.Other.Id, "Developer"), default);
        await f.Management.AcceptAppInvitationAsync(f.Other.Id, invitation.Id, default);
        var organization = await f.Db.Developers.SingleAsync(); organization.Status = DeveloperStatus.Suspended; await f.Db.SaveChangesAsync();
        Assert.NotNull(await f.Management.GetOwnedAsync(f.Other.Id, app.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateOwnedAsync(f.Other.Id, app.Id, f.Input(), default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.InviteAppMemberAsync(f.Owner.Id, app.Id, new(Guid.NewGuid()), default));
    }
}
