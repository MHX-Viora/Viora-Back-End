using Viora.Application.Notifications;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class PostNotificationFactoryTests
{
    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã thích bài viết của bạn.", "Đã thích bài viết này.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã thích video của bạn.", "Đã thích video này.")]
    public void Create_builds_like_notification_text_by_post_type(
        PostType postType,
        string expectedTitle,
        string expectedContent)
    {
        var sender = Sender();
        var referenceId = Guid.NewGuid();

        var notification = PostNotificationFactory.Create(
            Guid.NewGuid(),
            sender,
            NotificationType.PostLike,
            postType,
            referenceId);

        Assert.Equal(expectedTitle, notification.Title);
        Assert.Equal(expectedContent, notification.Content);
        Assert.Equal(sender.AvatarUrl, notification.ImageUrl);
        Assert.Equal(referenceId, notification.ReferenceId);
        Assert.Equal(NotificationReferenceType.Post, notification.ReferenceType);
    }

    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã bình luận bài viết của bạn.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã bình luận video của bạn.")]
    public void Create_builds_comment_notification_text_by_post_type(
        PostType postType,
        string expectedTitle)
    {
        var notification = PostNotificationFactory.Create(
            Guid.NewGuid(),
            Sender(),
            NotificationType.PostComment,
            postType,
            Guid.NewGuid());

        Assert.Equal(expectedTitle, notification.Title);
        Assert.Equal(NotificationReferenceType.Post, notification.ReferenceType);
    }

    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã trả lời bình luận trong bài viết của bạn.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã trả lời bình luận trong video của bạn.")]
    public void Create_builds_reply_notification_text_and_comment_reference_by_post_type(
        PostType postType,
        string expectedTitle)
    {
        var notification = PostNotificationFactory.Create(
            Guid.NewGuid(),
            Sender(),
            NotificationType.CommentReply,
            postType,
            Guid.NewGuid());

        Assert.Equal(expectedTitle, notification.Title);
        Assert.Equal(NotificationReferenceType.Comment, notification.ReferenceType);
    }

    [Theory]
    [InlineData(PostType.Post, "Nguyễn Văn A đã nhắc đến bạn trong một bài viết.")]
    [InlineData(PostType.ShortVideo, "Nguyễn Văn A đã nhắc đến bạn trong một video.")]
    public void Create_builds_mention_notification_text_by_post_type(
        PostType postType,
        string expectedTitle)
    {
        var notification = PostNotificationFactory.Create(
            Guid.NewGuid(),
            Sender(),
            NotificationType.Mention,
            postType,
            Guid.NewGuid());

        Assert.Equal(expectedTitle, notification.Title);
    }

    private static User Sender() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Nguyễn Văn A",
        AvatarUrl = "https://cdn.example/avatar.jpg"
    };
}
