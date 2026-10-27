using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppRestoreEndpointTests
{
    [Fact]
    public async Task ModerationRequiresLiveBearerAdminAndStrictCommandsWithNoDeveloperOverride()
    {
        await using var host = await MiniAppApiTests.Host.Create(sandbox: true, review: true); var http = host.Client;
        var token = await host.Login("moderation-endpoint@example.test"); http.DefaultRequestHeaders.Authorization = new("DeveloperSession", token);
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Moderation", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var actor = await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>(); var account = new Account { Email = "moderation-admin@example.test", Role = AccountRole.Admin }; var sid = Guid.NewGuid();
            db.Add(new RefreshToken { Account = account, SessionId = sid, TokenHash = "moderation-admin-fixture", ExpiresAt = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync(); return (account.Id, sid, Revision: await db.MiniApps.Select(x => x.Version).SingleAsync());
        });
        var path = "/api/admin/mini-apps/" + app.AppId;
        var command = new MiniAppModerationRequest(Guid.NewGuid(), actor.Revision, "Live admin is investigating application safety.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path + "/suspend", command)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", host.AdminToken(actor.Id, actor.sid));
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path + "/suspend?actor=admin", command)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path + "/suspend", new { command.RequestId, command.ConcurrencyToken, command.Reason, status = "Active" })).StatusCode);
        var response = await http.PostAsJsonAsync(path + "/suspend", command); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString()); var suspended = (await response.Content.ReadFromJsonAsync<MiniAppModerationResultDto>())!;
        var restore = MiniAppRestoreTests.Command(suspended);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path + "/restore", restore)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", host.AdminToken(actor.Id, actor.sid));
        var restored = await http.PostAsJsonAsync(path + "/restore", restore); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("Active", (await restored.Content.ReadFromJsonAsync<MiniAppModerationResultDto>())!.Operational.Status);
        await host.WithServices(async services => { await services.GetRequiredService<AppDbContext>().Accounts.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, AccountRole.Moderator)); return true; });
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync(path + "/restore", restore)).StatusCode);
    }
}
