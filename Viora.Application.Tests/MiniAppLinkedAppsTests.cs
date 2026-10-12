using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using viora_BE.Controllers;
using Xunit;

public sealed class MiniAppLinkedAppsTests
{
    [Fact] public async Task ListOnlyCurrentOwnersGrantedAppsIncludingInactiveAndPublishedMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "owner@example.com" }; var other = new Account { Email = "other@example.com" };
        var developer = new Developer { Name = "Developer", Email = "developer@example.com", Status = DeveloperStatus.Suspended };
        var permission = await db.MiniAppPermissions.SingleAsync(p => p.Code == "identity.login");
        MiniApp App(string slug) => new() { Name = "Draft edit", Slug = slug, WebUrl = "https://partner.example/", CallbackUrl = "https://partner.example/callback", Developer = developer, ClientId = slug, ClientSecretHash = "private-hash", Status = MiniAppStatus.Suspended,
            PublishedConfigurationJson = "{\"Name\":\"Published name\",\"IconUrl\":\"https://partner.example/logo.png\"}" };
        var mine = App("mine"); var theirs = App("theirs"); var revoked = App("revoked");
        db.AddRange(owner, other, developer, mine, theirs, revoked);
        db.AddRange(new MiniAppUserConsent { Account = owner, MiniApp = mine, Permission = permission, Granted = true },
            new MiniAppUserConsent { Account = other, MiniApp = theirs, Permission = permission, Granted = true },
            new MiniAppUserConsent { Account = owner, MiniApp = revoked, Permission = permission, Granted = false });
        await db.SaveChangesAsync();
        var controller = Controller(db, owner.Id.ToString());
        var result = Assert.IsType<OkObjectResult>(await controller.List(default));
        var body = JsonSerializer.SerializeToElement(result.Value);
        var app = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(mine.Id, app.GetProperty("Id").GetGuid());
        Assert.Equal("Published name", app.GetProperty("Name").GetString());
        Assert.False(app.GetProperty("Available").GetBoolean());
        Assert.Equal("Suspended", app.GetProperty("Status").GetString());
        Assert.DoesNotContain("private-hash", body.GetRawText());
        Assert.Equal("identity.login", Assert.Single(app.GetProperty("Permissions").EnumerateArray()).GetString());
        // Revoke is owner-filtered; removing another account's consent is impossible.
        var consentController = new MiniAppConsentController(new MiniAppService(db, new ClientCredentialService())) { ControllerContext = controller.ControllerContext };
        Assert.IsType<NoContentResult>(await consentController.Revoke(theirs.Id, default));
        Assert.True(await db.MiniAppUserConsents.AnyAsync(c => c.AccountId == other.Id && c.MiniAppId == theirs.Id && c.Granted));
    }

    [Fact] public async Task MissingOrInactiveMainAccountCannotReadLinkedApps()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        Assert.IsType<UnauthorizedResult>(await Controller(db, "not-an-account").List(default));
        var deleted = new Account { Email = "deleted@example.com", DeletedAt = DateTime.UtcNow }; db.Add(deleted); await db.SaveChangesAsync();
        Assert.IsType<UnauthorizedResult>(await Controller(db, deleted.Id.ToString()).List(default));
    }

    static MiniAppLinkedAppsController Controller(AppDbContext db, string accountId) => new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", accountId)], "Bearer")) } } };
}
