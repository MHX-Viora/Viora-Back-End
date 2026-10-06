using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Application.Tests.Live;

public sealed class LiveViewerEndTests
{
    [Theory]
    [InlineData(LiveStatus.Ended)]
    [InlineData(LiveStatus.Interrupted)]
    [InlineData(LiveStatus.Cancelled)]
    [InlineData(LiveStatus.Reconnecting)]
    public async Task Viewer_can_read_terminal_or_reconnecting_status(LiveStatus status)
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var live = await scope.Db.Lives.SingleAsync();
        live.Status = status;
        live.Privacy = LivePrivacy.Public;
        await scope.Db.SaveChangesAsync();
        var controller = Controller(scope);
        var response = Assert.IsType<OkObjectResult>(await controller.Get(scope.LiveId, default));
        Assert.Equal(status, Assert.IsType<LiveDto>(response.Value).Status);
    }

    [Theory]
    [InlineData(LivePrivacy.Followers)]
    [InlineData(LivePrivacy.Friends)]
    public async Task Ended_live_retains_privacy_restrictions(LivePrivacy privacy)
    {
        await using var scope = await LiveGiftWalletIntegrationTests.GiftScope.CreateAsync();
        var live = await scope.Db.Lives.SingleAsync();
        live.Status = LiveStatus.Ended;
        live.Privacy = privacy;
        await scope.Db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await Controller(scope).Get(scope.LiveId, default));
    }

    private static LivesController Controller(LiveGiftWalletIntegrationTests.GiftScope scope) =>
        new(scope.Db, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("user_id", scope.SenderId.ToString())], "test"))
                }
            }
        };
}
