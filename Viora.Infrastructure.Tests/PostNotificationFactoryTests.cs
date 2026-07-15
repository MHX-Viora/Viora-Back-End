using Viora.Application.Notifications;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class PostNotificationFactoryTests
{
    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã bày tỏ cảm xúc với bài viết của bạn.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã bày tỏ cảm xúc với video của bạn.")]
    public void Create_builds_like_notification_text_by_post_type(
        PostType postType,
        string expectedContent)
    {
        var sender = Sender();
        var referenceId = Guid.NewGuid();

        var notification = NotificationFactory.Create(
            Guid.NewGuid(),
            NotificationType.PostLike,
            sender,
            referenceId,
            NotificationReferenceType.Post,
            postType);

        Assert.Equal("Cảm xúc", notification.Title);
        Assert.Equal(expectedContent, notification.Content);
        Assert.Null(notification.ImageUrl);
        Assert.Equal(referenceId, notification.ReferenceId);
        Assert.Equal(NotificationReferenceType.Post, notification.ReferenceType);
    }

    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã bình luận bài viết của bạn.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã bình luận video của bạn.")]
    public void Create_builds_comment_notification_text_by_post_type(
        PostType postType,
        string expectedContent)
    {
        var notification = NotificationFactory.Create(
            Guid.NewGuid(),
            NotificationType.PostComment,
            Sender(),
            Guid.NewGuid(),
            NotificationReferenceType.Post,
            postType);

        Assert.Equal("Bình luận", notification.Title);
        Assert.Equal(expectedContent, notification.Content);
        Assert.Equal(NotificationReferenceType.Post, notification.ReferenceType);
    }

    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã trả lời bình luận trong bài viết của bạn.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã trả lời bình luận trong video của bạn.")]
    public void Create_builds_reply_notification_text_and_comment_reference_by_post_type(
        PostType postType,
        string expectedContent)
    {
        var notification = NotificationFactory.Create(
            Guid.NewGuid(),
            NotificationType.CommentReply,
            Sender(),
            Guid.NewGuid(),
            NotificationReferenceType.Comment,
            postType);

        Assert.Equal("Phản hồi", notification.Title);
        Assert.Equal(expectedContent, notification.Content);
        Assert.Equal(NotificationReferenceType.Comment, notification.ReferenceType);
    }

    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã nhắc đến bạn trong một bài viết.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã nhắc đến bạn trong một video.")]
    public void Create_builds_mention_notification_text_by_post_type(
        PostType postType,
        string expectedContent)
    {
        var notification = NotificationFactory.Create(
            Guid.NewGuid(),
            NotificationType.Mention,
            Sender(),
            Guid.NewGuid(),
            NotificationReferenceType.Post,
            postType);

        Assert.Equal("Nhắc đến bạn", notification.Title);
        Assert.Equal(expectedContent, notification.Content);
    }

    [Fact]
    public void Create_builds_social_notification_title_and_content()
    {
        var notification = NotificationFactory.Create(
            Guid.NewGuid(),
            NotificationType.Follow,
            Sender(),
            Guid.NewGuid(),
            NotificationReferenceType.User);

        Assert.Equal("Theo dõi", notification.Title);
        Assert.Equal("Nguyễn Văn A đã bắt đầu theo dõi bạn.", notification.Content);
        Assert.Null(notification.ImageUrl);
    }

    private static User Sender() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Nguyễn Văn A",
        AvatarUrl = "https://cdn.example/avatar.jpg"
    };
}
