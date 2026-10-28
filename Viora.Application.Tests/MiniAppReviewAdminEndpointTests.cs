using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewAdminEndpointTests
{
    [Fact]
    public async Task AdminQueueRequiresLiveBearerAdminAndStrictFiltersWithoutDeveloperBffAuthority()
    {
        await using var host = await MiniAppApiTests.Host.Create(sandbox: true, review: true); var http = host.Client;
        var root = "/api/admin/mini-app-reviews";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(root)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("review-admin-boundary@example.test"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(root)).StatusCode);
        var actor = await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>(); var admin = new Account { Email = "queue-admin@example.test", Role = AccountRole.Admin }; var sid = Guid.NewGuid();
            db.Add(new RefreshToken { Account = admin, SessionId = sid, TokenHash = "queue-fixture", ExpiresAt = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync(); return (admin.Id, sid);
        });
        http.DefaultRequestHeaders.Authorization = new("Bearer", host.AdminToken(actor.Id, actor.sid));
        var response = await http.GetAsync(root); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Empty((await response.Content.ReadFromJsonAsync<MiniAppReviewAdminPageDto>())!.Items);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(root + "/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/admin/mini-apps")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/admin/mini-apps/app_missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(root + "/" + Guid.NewGuid())).StatusCode);
        foreach (var query in new[] { "?actor=developer", "?page=1&page=2", "?pageSize=101", "?sort=sql", "?status=4", "?assignee=not-a-guid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(root + query)).StatusCode);
        var unknown = root + "/" + Guid.NewGuid(); var command = new MiniAppReviewCommand(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(unknown + "/start", command)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(unknown + "/start", new { command.RequestId, command.ConcurrencyToken, actor = actor.Id })).StatusCode);
        foreach (var route in new[] { "reject", "request-changes", "approve" })
            Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(unknown + "/" + route, new MiniAppReviewDecisionRequest(Guid.NewGuid(), Guid.NewGuid(),
                "A valid review reason for endpoint validation", route == "request-changes" ? [new("description", "Please correct this description.")] : null))).StatusCode);
        await host.WithServices(async services => { await services.GetRequiredService<AppDbContext>().Accounts.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, AccountRole.Moderator)); return true; });
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(root)).StatusCode);
    }
}
