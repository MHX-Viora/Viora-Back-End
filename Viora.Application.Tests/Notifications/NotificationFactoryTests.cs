using Viora.Application.Notifications;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Notifications;

public sealed class NotificationFactoryTests
{
    [Fact]
    public void ArticleInteractionUsesArticleWording()
    {
        var notification = NotificationFactory.Create(
            Guid.NewGuid(),
            NotificationType.PostComment,
            new User { Id = Guid.NewGuid(), DisplayName = "Người đọc" },
            Guid.NewGuid(),
            NotificationReferenceType.Post,
            PostType.Article);

        Assert.Contains("bài báo", notification.Content);
    }

    [Fact]
    public void ArticleInteractionUsesArticleReferenceType() =>
        Assert.Equal(
            NotificationReferenceType.Article,
            ReactPostHandler.GetNotificationReferenceType(
                PostType.Article,
                NotificationType.PostLike));
}
