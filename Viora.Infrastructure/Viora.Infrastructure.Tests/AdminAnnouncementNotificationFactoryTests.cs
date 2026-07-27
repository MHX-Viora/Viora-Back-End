using Viora.Application.Admin;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class AdminAnnouncementNotificationFactoryTests
{
    [Fact]
    public void Create_builds_a_senderless_system_notification()
    {
        var recipientId = Guid.NewGuid();

        var notification = AdminAnnouncementNotificationFactory.Create(
            recipientId,
            "Bảo trì hệ thống",
            "Viora sẽ bảo trì lúc 23:00.",
            "https://example.test/maintenance.png");

        Assert.Equal(recipientId, notification.UserId);
        Assert.Equal(NotificationType.System, notification.NotificationType);
        Assert.Null(notification.SenderUserId);
        Assert.Null(notification.ReferenceId);
        Assert.Null(notification.ReferenceType);
        Assert.Equal("Bảo trì hệ thống", notification.Title);
        Assert.Equal("Viora sẽ bảo trì lúc 23:00.", notification.Content);
        Assert.Equal("https://example.test/maintenance.png", notification.ImageUrl);
    }
}
