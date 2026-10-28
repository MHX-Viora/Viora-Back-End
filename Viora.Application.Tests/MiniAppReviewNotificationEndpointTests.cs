using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class MiniAppReviewNotificationEndpointTests
{
    [Fact]
    public async Task InboxIsBoundedRecipientScopedAndMarkReadIsMonotonic()
    {
        await using var f = await SandboxFixture.Create(); await MiniAppPublishingReadinessTests.Approved(f);
        var service = new MiniAppReviewNotificationService(f.Db, f.Clock);
        var page = await service.ListAsync(f.Actor.Id, f.Source.Id, 1, 2, false, default);
        Assert.Equal(3, page.UnreadCount); Assert.Equal(3, page.Total); Assert.Equal(2, page.Items.Count);
        var notification = page.Items[0]; Assert.StartsWith("/developers/console/apps/", notification.DeepLink);
        Assert.Contains("?tab=review", notification.DeepLink); Assert.DoesNotContain("partner.example.com", notification.DeepLink);
        var read = await service.MarkReadAsync(f.Actor.Id, f.Source.Id, notification.Id, default); Assert.NotNull(read.ReadAt);
        f.Clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(read.ReadAt, (await service.MarkReadAsync(f.Actor.Id, f.Source.Id, notification.Id, default)).ReadAt);
        var unread = await service.ListAsync(f.Actor.Id, f.Source.Id, 1, 20, true, default);
        Assert.Equal(2, unread.Total); Assert.Equal(2, unread.UnreadCount); Assert.DoesNotContain(unread.Items, x => x.Id == notification.Id);
        var other = new DeveloperAccount { EmailNormalized = "other-inbox@example.test", Status = DeveloperStatus.Active };
        var source = new DeveloperSession { Account = other, TokenHash = "other-inbox", ExpiresAt = f.Clock.Now.UtcDateTime.AddHours(1), AbsoluteExpiresAt = f.Clock.Now.UtcDateTime.AddHours(2) };
        f.Db.Add(source); await f.Db.SaveChangesAsync();
        Assert.Empty((await service.ListAsync(other.Id, source.Id, 1, 20, false, default)).Items);
        var denied = await Assert.ThrowsAsync<DeveloperAuthException>(() => service.MarkReadAsync(other.Id, source.Id, notification.Id, default)); Assert.Equal(404, denied.Status);
        await f.Db.DeveloperSessions.Where(x => x.Id == f.Source.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => service.ListAsync(f.Actor.Id, f.Source.Id, 1, 20, false, default));
    }

    [Fact]
    public async Task HttpInboxRequiresDeveloperSessionRejectsActorFieldsAndPreservesRealModerationReceipt()
    {
        await using var host = await MiniAppApiTests.Host.Create(sandbox: true, review: true); var http = host.Client; var root = "/api/developer/review-notifications";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(root)).StatusCode);
        var token = await host.Login("inbox-http@example.test"); http.DefaultRequestHeaders.Authorization = new("DeveloperSession", token);
        var app = (await (await http.PostAsJsonAsync("/api/developer/mini-apps", new MiniAppCreateRequest("Inbox", "utilities", Guid.NewGuid()))).Content.ReadFromJsonAsync<MiniAppDto>())!;
        var actor = await host.WithServices(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>(); var admin = new Account { Role = AccountRole.Admin, Email = "inbox-admin@example.test" }; var sid = Guid.NewGuid();
            db.Add(new RefreshToken { Account = admin, SessionId = sid, TokenHash = "inbox-admin", ExpiresAt = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync();
            return (admin.Id, sid, Revision: await db.MiniApps.Select(x => x.Version).SingleAsync());
        });
        http.DefaultRequestHeaders.Authorization = new("Bearer", host.AdminToken(actor.Id, actor.sid));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/admin/mini-apps/" + app.AppId + "/suspend", new MiniAppModerationRequest(Guid.NewGuid(), actor.Revision, "Application safety review requires suspension."))).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", token);
        var response = await http.GetAsync(root + "?page=1&pageSize=20&unread=true"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString()); var page = (await response.Content.ReadFromJsonAsync<MiniAppReviewNotificationPageDto>())!;
        var receipt = Assert.Single(page.Items); Assert.Equal("Suspended", receipt.EventType); Assert.Equal(app.AppId, receipt.AppId);
        foreach (var query in new[] { "?recipient=" + actor.Id, "?page=1&page=2", "?pageSize=101", "?unread=1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(root + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(root + "/" + receipt.Id + "/read", new { recipient = actor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(root + "/" + receipt.Id + "/read", new { })).StatusCode);
        Assert.Equal(0, (await http.GetFromJsonAsync<MiniAppReviewNotificationPageDto>(root))!.UnreadCount);
        http.DefaultRequestHeaders.Authorization = new("DeveloperSession", await host.Login("inbox-other-http@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(root + "/" + receipt.Id + "/read", new { })).StatusCode);
    }
}
