using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Xunit;
using Fixture = MiniAppHybridIntegrationTests.Fixture;

public sealed class DeveloperRegistrationTests
{
    private static DeveloperInput Input => new("Developer", null, "owner@example.com", null, null, null);

    [Fact]
    public async Task RejectedOwnerCanCorrectResubmitAndCreateOnlyAfterApproval()
    {
        await using var f = await Fixture.Create();
        var profile = await f.Management.RegisterDeveloperAsync(f.Owner.Id, Input, default);
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, profile.Id, DeveloperStatus.Rejected, default);
        Assert.Contains(await f.Db.MiniAppAuditLogs.ToListAsync(), x => x.Action == "DeveloperRejected");
        var corrected = await f.Management.UpdateProfileAsync(f.Owner.Id, Input with { Name = "Corrected" }, default);
        Assert.Equal(profile.Id, corrected.Id);
        Assert.Equal("Pending", corrected.Status);
        Assert.Contains(await f.Db.MiniAppAuditLogs.ToListAsync(), x => x.Action == "DeveloperResubmitted");
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.CreateAsync(f.Owner.Id, f.Input(), default));
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, profile.Id, DeveloperStatus.Active, default);
        Assert.NotEqual(Guid.Empty, (await f.Management.CreateAsync(f.Owner.Id, f.Input(), default)).Id);
    }

    [Theory]
    [InlineData(DeveloperStatus.Pending, DeveloperStatus.Suspended)]
    [InlineData(DeveloperStatus.Active, DeveloperStatus.Rejected)]
    [InlineData(DeveloperStatus.Active, DeveloperStatus.Active)]
    [InlineData(DeveloperStatus.Rejected, DeveloperStatus.Active)]
    [InlineData(DeveloperStatus.Suspended, DeveloperStatus.Rejected)]
    public async Task InvalidAdminDecisionsDoNotChangeProfileOrAudit(DeveloperStatus from, DeveloperStatus to)
    {
        await using var f = await Fixture.Create(true);
        var developer = await f.Db.Developers.SingleAsync();
        developer.Status = from; await f.Db.SaveChangesAsync();
        var count = await f.Db.MiniAppAuditLogs.CountAsync();
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.SetDeveloperStatusAsync(f.Other.Id, developer.Id, to, default));
        Assert.Equal("INVALID_STATUS_TRANSITION", error.Code);
        Assert.Equal(from, developer.Status);
        Assert.Equal(count, await f.Db.MiniAppAuditLogs.CountAsync());
    }

    [Fact]
    public async Task InvalidCorrectionsAndNonOwnersCannotResubmit()
    {
        await using var f = await Fixture.Create();
        var profile = await f.Management.RegisterDeveloperAsync(f.Owner.Id, Input, default);
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, profile.Id, DeveloperStatus.Rejected, default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateProfileAsync(f.Owner.Id, Input with { Email = "invalid" }, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateProfileAsync(f.Other.Id, Input, default));
        Assert.Equal("Rejected", (await f.Management.GetProfileAsync(f.Owner.Id, default))!.Status);
    }

    [Theory]
    [InlineData("Developer <owner@example.com>")]
    [InlineData("owner@example.com,other@example.com")]
    public async Task RegistrationRequiresPlainEmailAddress(string email)
    {
        await using var f = await Fixture.Create();
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RegisterDeveloperAsync(f.Owner.Id, Input with { Email = email }, default));
        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.Empty(await f.Db.Developers.ToListAsync());
        Assert.Empty(await f.Db.DeveloperMemberships.ToListAsync());
    }

    [Fact]
    public async Task RegistrationNormalizesSurroundingWhitespace()
    {
        await using var f = await Fixture.Create();
        var profile = await f.Management.RegisterDeveloperAsync(f.Owner.Id, Input with { Email = " OWNER@example.com ", Website = " https://example.com " }, default);
        Assert.Equal("owner@example.com", profile.Email);
        Assert.Equal("https://example.com", profile.Website);
    }

    [Fact]
    public async Task DuplicateRegistrationDoesNotCreateAnotherProfileOrMembership()
    {
        await using var f = await Fixture.Create();
        await f.Management.RegisterDeveloperAsync(f.Owner.Id, Input, default);
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RegisterDeveloperAsync(f.Owner.Id, Input, default));
        Assert.Equal("DEVELOPER_EXISTS", error.Code);
        error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RegisterDeveloperAsync(f.Other.Id, Input with { Email = " OWNER@example.com " }, default));
        Assert.Equal("DEVELOPER_EXISTS", error.Code);
        Assert.Single(await f.Db.Developers.ToListAsync());
        Assert.Single(await f.Db.DeveloperMemberships.ToListAsync());
        Assert.Null(await f.Management.GetProfileAsync(f.Other.Id, default));
    }

    [Fact]
    public async Task ActiveEditsDoNotResetApprovalAndSuspendedOwnersCannotEdit()
    {
        await using var f = await Fixture.Create(true);
        var updated = await f.Management.UpdateProfileAsync(f.Owner.Id, Input, default);
        Assert.Equal("Active", updated.Status);
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, updated.Id, DeveloperStatus.Suspended, default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateProfileAsync(f.Owner.Id, Input with { Name = "Changed" }, default));
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, updated.Id, DeveloperStatus.Active, default);
        Assert.Contains(await f.Db.MiniAppAuditLogs.ToListAsync(), x => x.Action == "DeveloperRestored");
        Assert.Equal("Developer", (await f.Management.GetProfileAsync(f.Owner.Id, default))!.Name);
    }
}
