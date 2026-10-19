using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppSandboxPolicyTests
{
    [Fact]
    public async Task AdminNeedsExplicitAllowlistLiveAdminRoleAndActiveSocialSession()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?,int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new DeveloperAccount {EmailNormalized="owner@admin-test.test",Status=DeveloperStatus.Active};
        var app = new MiniApp {Developer=owner,AppId="app_admin_policy",Name="Admin test",Category="utilities"};
        var admin = new Account {Email="admin@sandbox.test",Role=AccountRole.Admin}; var sessionId=Guid.NewGuid();
        db.MiniApps.Add(app); db.Accounts.Add(admin);
        db.RefreshTokens.Add(new() {AccountId=admin.Id,SessionId=sessionId,TokenHash="sandbox-admin",ExpiresAt=DateTime.UtcNow.AddHours(1)});
        await db.SaveChangesAsync();
        var options=new MiniAppSandboxOptions {Enabled=true}; var access=new MiniAppSandboxAccess(db,TimeProvider.System,options);
        var actor=new SandboxActor(SandboxActorKind.Admin,admin.Id,sessionId);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>access.RequireAsync(actor,app.AppId,default));
        options.AdminTesterIds=[admin.Id]; Assert.Equal(app.Id,(await access.RequireAsync(actor,app.AppId,default)).Id);
        admin.Role=AccountRole.User; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>access.RequireAsync(actor,app.AppId,default));
        admin.Role=AccountRole.Admin; (await db.RefreshTokens.SingleAsync()).RevokedAt=DateTime.UtcNow; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>access.RequireAsync(actor,app.AppId,default));
    }

    [Fact]
    public void DisabledOptionsNeedNoHostButEnabledOriginsAndLimitsFailClosed()
    {
        var options = new MiniAppSandboxOptions(); var validator = new MiniAppSandboxOptionsValidator();
        Assert.False(options.Enabled); Assert.True(validator.Validate(null, options).Succeeded);
        options.Enabled = true; Assert.True(validator.Validate(null, options).Failed);
        options.PortalOrigin = "https://portal.ankt.example"; options.HostOrigin = "https://sandbox.ankt.example";
        options.Issuer = "https://identity.ankt.example/"; Assert.True(validator.Validate(null, options).Succeeded);
        foreach (var origin in new[] {options.PortalOrigin, "https://identity.ankt.example", "http://sandbox.ankt.example", "https://sandbox.ankt.example/path", "https://user:password@sandbox.ankt.example"})
        { options.HostOrigin = origin; Assert.True(validator.Validate(null, options).Failed); }
        options.HostOrigin = "https://sandbox.ankt.example"; options.SessionMinutes = 31;
        Assert.True(validator.Validate(null, options).Failed);
    }

    [Fact]
    public async Task AccessRequiresActualOwnerAndLiveSourceSessionAndNeverConvertsDeveloperToUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?,int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new DeveloperAccount {FullName="Owner",EmailNormalized="owner@sandbox.test",Status=DeveloperStatus.Active};
        var session = new DeveloperSession {Account=owner,TokenHash="test-source",ExpiresAt=DateTime.UtcNow.AddHours(1),AbsoluteExpiresAt=DateTime.UtcNow.AddHours(2)};
        var app = new MiniApp {Developer=owner,AppId="app_sandbox_policy",Name="Sandbox policy",Category="utilities"};
        db.DeveloperSessions.Add(session); db.MiniApps.Add(app); await db.SaveChangesAsync();
        var policy = new MiniAppSandboxAccess(db, TimeProvider.System, new MiniAppSandboxOptions {Enabled=true});
        var actor = new SandboxActor(SandboxActorKind.Developer,owner.Id,session.Id);
        Assert.Equal(app.Id,(await policy.RequireAsync(actor,app.AppId,default)).Id);
        var denied = await Assert.ThrowsAsync<DeveloperAuthException>(()=>policy.RequireAsync(actor with {Id=Guid.NewGuid()},app.AppId,default));
        Assert.Equal(404,denied.Status);
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>policy.RequireAsync(actor with {Kind=SandboxActorKind.Admin},app.AppId,default));
        session.RevokedAt=DateTime.UtcNow; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DeveloperAuthException>(()=>policy.RequireAsync(actor,app.AppId,default));
        Assert.Empty(db.AnktSsoSessions); Assert.Empty(db.MiniAppUserConsents);
    }
}
