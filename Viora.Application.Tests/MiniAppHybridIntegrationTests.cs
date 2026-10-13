using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Security;
using Xunit;

public sealed class MiniAppHybridIntegrationTests
{
    [Fact] public async Task RetiredCustomProtocolRejectsAuthorizeAndExchangeBeforeReadingDatabase()
    {
        await using var f = await Fixture.Create();
        var authorize = await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid(), "obsolete", "https://example.com/callback", Fixture.State, MiniAppSecurityPolicy.CreatePkceChallenge(Fixture.Verifier), "S256"), default));
        var exchange = await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.ExchangeAsync(new("obsolete", null, "obsolete", "https://example.com/callback", Fixture.State, Fixture.Verifier), default));
        Assert.Equal("LEGACY_PROTOCOL_REMOVED", authorize.Code); Assert.Equal(410, authorize.StatusCode);
        Assert.Equal("LEGACY_PROTOCOL_REMOVED", exchange.Code); Assert.Equal(410, exchange.StatusCode);
    }
    [Fact] public async Task AutoLoginUsesPublishedPolicyAndIndependentAppsCannotEnableIt()
    {
        await using var f = await Fixture.Create(true);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.CreateAsync(f.Owner.Id, f.Input() with { AutoLogin = true }, default));
        f.Db.ChangeTracker.Clear(); // The failed HTTP request would dispose its scoped context.
        var app = await f.Publish(f.SsoInput() with { AutoLogin = true });
        Assert.True((await f.Runtime.GetActiveDetailAsync(app.Id, default))!.AutoLogin);
        await f.Management.UpdateOwnedAsync(f.Owner.Id, app.Id, f.SsoInput() with { AutoLogin = false }, default);
        Assert.True((await f.Runtime.GetActiveDetailAsync(app.Id, default))!.AutoLogin);
        Assert.False((await f.Management.GetOwnedAsync(f.Owner.Id, app.Id, default))!.AutoLogin);
    }
    [Fact] public async Task RegistrationBindsAuthenticatedOwnerAndNeedsAdminApproval()
    {
        await using var f = await Fixture.Create();
        var profile = await f.Management.RegisterDeveloperAsync(f.Owner.Id, new("Developer", null, "owner@example.com", null, null, f.Other.Id), default);
        Assert.Equal(f.Owner.Id, profile.AccountId); Assert.Equal("Pending", profile.Status); Assert.Equal("Owner", profile.MembershipRole);
        Assert.Null(await f.Management.GetProfileAsync(f.Other.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.CreateAsync(f.Owner.Id, f.Input(), default));
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, profile.Id, DeveloperStatus.Active, default);
        Assert.NotEqual(Guid.Empty, (await f.Management.CreateAsync(f.Owner.Id, f.Input(), default)).Id);
    }
    [Fact] public async Task IndependentAppLaunchesWebsiteWithoutCodeOrConsent()
    {
        await using var f = await Fixture.Create(true);
        var app = await f.Publish(f.Input()); var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        Assert.False(launch.RequiresConsent); Assert.Equal("https://example.com/app", launch.LaunchUrl); Assert.DoesNotContain("code=", launch.LaunchUrl!);
        Assert.NotNull(launch.SessionToken);
        Assert.True((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, new(launch.SessionId!.Value, launch.SessionToken!), default)).Active);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.AuthorizeAsync(f.Owner.Id, app.Id, f.Auth(launch), default));
    }
    [Fact] public async Task OwnershipAndTeamSeparateMemberEditsFromOwnerCredentials()
    {
        await using var f = await Fixture.Create(true); var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        Assert.Null(await f.Management.GetOwnedAsync(f.Other.Id, created.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateOwnedAsync(f.Other.Id, created.Id, f.Input(), default));
        var member = await f.Management.AddTeamAsync(f.Owner.Id, new(f.Other.Id), default);
        Assert.NotNull(await f.Management.GetOwnedAsync(f.Other.Id, created.Id, default));
        await f.Management.UpdateOwnedAsync(f.Other.Id, created.Id, f.Input() with { Name = "Updated by member" }, default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RotateSecretAsync(f.Other.Id, created.Id, default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.RemoveTeamAsync(f.Other.Id, member.Id, default));
        await f.Management.RemoveTeamAsync(f.Owner.Id, member.Id, default);
        Assert.Null(await f.Management.GetOwnedAsync(f.Other.Id, created.Id, default));
    }
    [Fact] public async Task ReviewRequiresVerifiedDomainsAndSubmittedSnapshotIsImmutable()
    {
        await using var f = await Fixture.Create(true); var created = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.SubmitReviewAsync(f.Owner.Id, created.Id, default)); Assert.Equal("DOMAIN_NOT_VERIFIED", error.Code);
        await f.Verify(created.Id); await f.Submit(created.Id);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.UpdateOwnedAsync(f.Owner.Id, created.Id, f.Input() with { Name = "Changed" }, default));
        await f.Management.SetAppStatusAsync(f.Other.Id, created.Id, MiniAppStatus.Rejected, "Needs changes", default);
        var versions = await f.Management.GetVersionsAsync(created.Id, default); Assert.Equal("Needs changes", Assert.Single(versions).Reason);
        await f.Management.UpdateOwnedAsync(f.Owner.Id, created.Id, f.Input() with { Name = "Fixed" }, default);
        await f.Submit(created.Id);
        await f.Management.SetAppStatusAsync(f.Other.Id, created.Id, MiniAppStatus.Active, null, default);
        Assert.Equal("Fixed", (await f.Runtime.GetActiveDetailAsync(created.Id, default))!.Name);
    }
    [Fact] public async Task PublishedConfigurationCannotChangeBeforeUpdateApprovalOrAfterRejection()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.Input());
        await f.Management.UpdateOwnedAsync(f.Owner.Id, app.Id, f.Input() with { Name = "New draft", WebUrl = "https://example.com/new" }, default);
        Assert.Equal("Example app", (await f.Runtime.GetActiveDetailAsync(app.Id, default))!.Name);
        Assert.Equal("https://example.com/app", (await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default)).LaunchUrl);
        await f.Submit(app.Id);
        await f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Rejected, "Rejected update", default);
        Assert.Equal("Active", (await f.Management.GetAppAsync(app.Id, default))!.Status);
        Assert.Equal("Example app", (await f.Runtime.GetActiveDetailAsync(app.Id, default))!.Name);
        await f.Management.UpdateOwnedAsync(f.Owner.Id, app.Id, f.Input() with { Name = "Approved update" }, default);
        await f.Submit(app.Id); await f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Active, null, default);
        Assert.Equal("Approved update", (await f.Runtime.GetActiveDetailAsync(app.Id, default))!.Name);
    }
    [Fact] public async Task RuntimeProjectsOnlyExplicitlyGrantedConsent()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        var session = new ValidateMiniAppSessionRequest(launch.SessionId!.Value, launch.SessionToken!);
        Assert.False(launch.RequiresConsent);
        Assert.Empty((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, session, default)).Permissions);
        await f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["identity.login", "profile.basic"], true), default);
        Assert.Equal(["identity.login", "profile.basic"], (await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, session, default)).Permissions.OrderBy(p => p).ToArray());
    }
    [Fact] public async Task IdentityConsentDoesNotGrantDeclinedOptionalProfilePermissions()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput() with { Permissions = ["identity.login", "profile.basic", "profile.email", "profile.phone"] });
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        await f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["identity.login"], true), default);
        Assert.Equal(["identity.login"], (await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, new(launch.SessionId!.Value, launch.SessionToken!), default)).Permissions);
    }
    [Theory] [InlineData("session")] [InlineData("token")] [InlineData("oversized")]
    public async Task InvalidRuntimeBindingsCannotValidateSession(string field)
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        var valid = new ValidateMiniAppSessionRequest(launch.SessionId!.Value, launch.SessionToken!);
        var invalid = field switch { "session" => valid with { SessionId = Guid.NewGuid() }, "token" => valid with { SessionToken = "wrong" }, _ => valid with { SessionToken = new string('a', 201) } };
        Assert.False((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, invalid, default)).Active);
        Assert.True((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, valid, default)).Active);
    }
    [Fact] public async Task ExpiredSessionCannotValidateOrCrossUserOrApp()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        var validation = new ValidateMiniAppSessionRequest(launch.SessionId!.Value, launch.SessionToken!);
        Assert.False((await f.Runtime.ValidateSessionAsync(f.Other.Id, app.Id, validation, default)).Active);
        Assert.False((await f.Runtime.ValidateSessionAsync(f.Owner.Id, Guid.NewGuid(), validation, default)).Active);
        await f.Db.MiniAppRuntimeSessions.ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        Assert.False((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, validation, default)).Active);
    }
    [Fact] public async Task RevokeSuspensionAndClosureInvalidateLiveRuntimePrivileges()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput()); await f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["identity.login", "profile.basic"], true), default);
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default); var request = new ValidateMiniAppSessionRequest(launch.SessionId!.Value, launch.SessionToken!);
        Assert.Contains("identity.login", (await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, request, default)).Permissions);
        await f.Runtime.RevokeConsentAsync(f.Owner.Id, app.Id, default); Assert.Empty((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, request, default)).Permissions);
        await f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Suspended, "Security", default);
        Assert.False((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, request, default)).Active);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default));
        await f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Active, null, default);
        await f.Runtime.CloseSessionAsync(f.Owner.Id, app.Id, request, default); Assert.False((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, request, default)).Active);
    }

    [Fact] public async Task ConsentRejectsUnapprovedScopesAndDraftPermissionsDoNotLeak()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["profile.email"], true), default));
        await f.Management.UpdateOwnedAsync(f.Owner.Id, app.Id, f.SsoInput() with { Permissions = ["identity.login", "profile.basic", "profile.email"] }, default);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["profile.email"], true), default));
        Assert.DoesNotContain((await f.Runtime.GetActiveDetailAsync(app.Id, default))!.Permissions, p => p.Code == "profile.email");
    }
    [Fact] public async Task SoftDeletedAccountCannotValidateAndRevokedConsentRemovesGrants()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        await f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["identity.login", "profile.basic"], true), default);
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        var validation = new ValidateMiniAppSessionRequest(launch.SessionId!.Value, launch.SessionToken!);
        await f.Runtime.RevokeConsentAsync(f.Owner.Id, app.Id, default);
        Assert.Empty((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, validation, default)).Permissions);
        f.Owner.DeletedAt = DateTime.UtcNow; await f.Db.SaveChangesAsync();
        Assert.False((await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, validation, default)).Active);
    }
    [Fact] public async Task DisabledPermissionIsRemovedFromActiveGrantsImmediately()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput()); await f.Runtime.GrantConsentAsync(f.Owner.Id, app.Id, new(["identity.login", "profile.basic"], true), default);
        await f.Management.SavePermissionAsync(f.Other.Id, "profile.basic", new("profile.basic", "Basic profile", null, false, false), default);
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        Assert.DoesNotContain("profile.basic", (await f.Runtime.ValidateSessionAsync(f.Owner.Id, app.Id, new(launch.SessionId!.Value, launch.SessionToken!), default)).Permissions);
        Assert.False((await f.Management.GetAdminPermissionsAsync(default)).Single(p => p.Code == "profile.basic").IsActive);
        Assert.DoesNotContain(await f.Management.GetPermissionsAsync(default), p => p.Code == "profile.basic");
    }
    [Fact] public async Task UnimplementedMediaAndArbitraryScopesCannotBeEnabled()
    {
        await using var f = await Fixture.Create(true);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.SavePermissionAsync(f.Other.Id, null, new("device.camera", "Camera", null, true), default));
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.SavePermissionAsync(f.Other.Id, null, new("wallet.pay", "Payment", null, true), default));
        Assert.DoesNotContain(await f.Management.GetAdminPermissionsAsync(default), p => p.Code == "device.camera" || p.Code == "wallet.pay");
    }
    [Fact] public async Task ArchivedAndSuspendedDeveloperAppsDisappearFromDiscovery()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.Input());
        var developer = await f.Db.Developers.SingleAsync(); await f.Management.SetDeveloperStatusAsync(f.Other.Id, developer.Id, DeveloperStatus.Suspended, default);
        Assert.Empty((await f.Runtime.GetActiveAsync(default)).Items); await Assert.ThrowsAsync<MiniAppException>(() => f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default));
        await f.Management.SetDeveloperStatusAsync(f.Other.Id, developer.Id, DeveloperStatus.Active, default); await f.Management.SetAppStatusAsync(f.Other.Id, app.Id, MiniAppStatus.Archived, "Archived", default);
        Assert.Empty((await f.Runtime.GetActiveAsync(default)).Items); Assert.Null(await f.Runtime.GetActiveDetailAsync(app.Id, default));
    }
    [Fact] public async Task ExpiryCleanupPreservesAuditAndDeletesOnlyExpiredRuntimeSessions()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        var expired = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        var live = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default);
        await f.Db.MiniAppRuntimeSessions.Where(s => s.Id == expired.SessionId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddDays(-2)));
        var auditCount = await f.Db.MiniAppAuditLogs.CountAsync();
        await MiniAppExpiryCleanup.CleanupAsync(f.Db, DateTime.UtcNow.AddDays(-1), default);
        Assert.Equal(live.SessionId, (await f.Db.MiniAppRuntimeSessions.AsNoTracking().SingleAsync()).Id);
        Assert.True(auditCount > 0); Assert.Equal(auditCount, await f.Db.MiniAppAuditLogs.CountAsync());
    }
    [Fact] public async Task LegacyPublishedWebsiteRemainsLaunchableButBridgeNeedsVerifiedDomains()
    {
        await using var f = await Fixture.Create(true); var app = await f.Publish(f.SsoInput());
        await f.Db.MiniAppVerifiedDomains.ExecuteDeleteAsync();
        Assert.Empty((await f.Runtime.GetActiveDetailAsync(app.Id, default))!.AllowedOrigins!);
        var launch = await f.Runtime.LaunchAsync(f.Owner.Id, app.Id, default); Assert.Equal("https://example.com/app", launch.LaunchUrl);
        // Website navigation uses published domains; the bridge uses verified origins above.
        Assert.Equal(new[] { "example.com" }, launch.AllowedDomains!);
    }
    [Fact] public async Task FailedDomainProofDoesNotVerifyAndAttemptCooldownIsPersistent()
    {
        await using var f = await Fixture.Create(true); var app = await f.Management.CreateAsync(f.Owner.Id, f.Input(), default);
        var domain = await f.Management.AddDomainAsync(f.Owner.Id, app.Id, new("example.com"), default);
        var rejecting = new MiniAppManagementService(f.Db, new ClientCredentialService(), new RejectingVerifier());
        await Assert.ThrowsAsync<MiniAppException>(() => rejecting.VerifyDomainAsync(f.Owner.Id, app.Id, domain.Id, default));
        Assert.Null((await f.Db.MiniAppVerifiedDomains.AsNoTracking().SingleAsync()).VerifiedAt);
        var error = await Assert.ThrowsAsync<MiniAppException>(() => f.Management.VerifyDomainAsync(f.Owner.Id, app.Id, domain.Id, default)); Assert.Equal(MiniAppErrorCodes.RateLimited, error.Code);
        await Assert.ThrowsAsync<MiniAppException>(() => f.Management.SubmitReviewAsync(f.Owner.Id, app.Id, default));
    }
    public sealed class Fixture : IAsyncDisposable
    {
        public const string State = "partner-transaction-state-123456789";
        public const string Verifier = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public required SqliteConnection Connection { get; init; }
        public required AppDbContext Db { get; init; }
        public required Account Owner { get; init; }
        public required Account Other { get; init; }
        public required MiniAppManagementService Management { get; init; }
        public required MiniAppService Runtime { get; init; }
        public MiniAppConfigurationInput Input() => new("Example app", "example-app", "Description", null, null, "https://example.com/app", "", ["example.com"], [], SupportEmail: "support@example.com", PrivacyPolicyUrl: "https://example.com/privacy");
        public MiniAppConfigurationInput SsoInput() => Input() with { CallbackUrl = "https://example.com/callback", CallbackUrls = ["https://example.com/callback"], AuthenticationMode = "AnktSso", Permissions = ["identity.login", "profile.basic"] };
        public AuthorizeMiniAppRequest Auth(LaunchMiniAppResponse launch) => new(launch.SessionId!.Value, launch.SessionToken!, "https://example.com/callback", State, MiniAppSecurityPolicy.CreatePkceChallenge(Verifier), "S256");
        public async Task Verify(Guid id) { var d = await Management.AddDomainAsync(Owner.Id, id, new("example.com"), default); await Management.VerifyDomainAsync(Owner.Id, id, d.Id, default); }
        public async Task Submit(Guid id)
        {
            // A prior run belongs to another configuration. Advance its persisted
            // cooldown for deterministic fixtures instead of sleeping or bypassing checks.
            await Db.MiniAppTechnicalTests.Where(t => t.MiniAppId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-1)));
            Assert.True((await Management.RunTechnicalTestAsync(Owner.Id, id, default)).Passed);
            await Management.SubmitReviewAsync(Owner.Id, id, default);
        }
        public async Task<MiniAppCredentialResponse> Publish(MiniAppConfigurationInput input) { var a = await Management.CreateAsync(Owner.Id, input, default); await Verify(a.Id); await Submit(a.Id); await Management.SetAppStatusAsync(Other.Id, a.Id, MiniAppStatus.Active, null, default); return a; }
        public static async Task<Fixture> Create(bool developer = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); connection.CreateFunction("char_length", (string value) => value.Length);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var owner = new Account { Email = "owner@example.com" }; var other = new Account { Email = "other@example.com" }; db.AddRange(owner, other, new User { AccountId = owner.Id, DisplayName = "Owner" });
            if (developer) db.Developers.Add(new Developer { Name = "Developer", Email = "dev@example.com", AccountId = owner.Id, Status = DeveloperStatus.Active }); await db.SaveChangesAsync();
            var credentials = new ClientCredentialService(); return new() { Connection = connection, Db = db, Owner = owner, Other = other, Management = new(db, credentials, new FakeVerifier(), new FakeTechnicalProbe()), Runtime = new(db, credentials) };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
        private sealed class FakeTechnicalProbe : IMiniAppTechnicalProbe
        {
            public Task<MiniAppTechnicalCheckDto> CheckAsync(string code, string url, CancellationToken ct)
                => Task.FromResult(new MiniAppTechnicalCheckDto(code, true, "Fixture HTTPS reachable", 200, 1));
        }
    }
    private sealed class FakeVerifier : IMiniAppDomainVerifier { public Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken cancellationToken) => Task.FromResult(true); }
    private sealed class RejectingVerifier : IMiniAppDomainVerifier { public Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken cancellationToken) => Task.FromResult(false); }
}
