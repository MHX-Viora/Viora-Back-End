using Viora.Domain.Entities;

namespace Viora.Application.Notifications;

public static class PostNotificationFactory
{
    public static Notification Create(
        Guid recipientUserId,
        User sender,
        NotificationType notificationType,
        PostType postType,
        Guid referenceId)
    {
        var text = GetText(notificationType, postType, sender.DisplayName);
        return new Notification
        {
            UserId = recipientUserId,
            SenderUserId = sender.Id,
            NotificationType = notificationType,
            ReferenceId = referenceId,
            ReferenceType = GetReferenceType(notificationType),
            Title = text.Title,
            Content = text.Content,
            ImageUrl = sender.AvatarUrl
        };
    }

    private static NotificationReferenceType GetReferenceType(NotificationType notificationType) =>
        notificationType == NotificationType.CommentReply
            ? NotificationReferenceType.Comment
            : NotificationReferenceType.Post;

    private static NotificationText GetText(
        NotificationType notificationType,
        PostType postType,
        string senderName)
    {
        var words = GetPostWords(postType);
        return notificationType switch
        {
            NotificationType.PostLike => new(
                $"{senderName} đã thích {words.Owner}.",
                $"Đã thích {words.This}."),
            NotificationType.PostComment => new(
                $"{senderName} đã bình luận {words.Owner}.",
                $"Đã bình luận {words.This}."),
            NotificationType.CommentReply => new(
                $"{senderName} đã trả lời bình luận trong {words.Owner}.",
                $"Đã trả lời bình luận trong {words.This}."),
            NotificationType.Mention => new(
                $"{senderName} đã nhắc đến bạn trong một {words.Singular}.",
                $"Đã nhắc đến bạn trong {words.This}."),
            NotificationType.PostShare => new(
                $"{senderName} đã chia sẻ {words.Owner}.",
                $"Đã chia sẻ {words.This}."),
            _ => new(
                $"{senderName} có tương tác mới với {words.Owner}.",
                $"Có tương tác mới với {words.This}.")
        };
    }

    private static PostWords GetPostWords(PostType postType) =>
        postType switch
        {
            PostType.ShortVideo => new("video", "video của bạn", "video này"),
            _ => new("bài viết", "bài viết của bạn", "bài viết này")
        };

    private sealed record PostWords(string Singular, string Owner, string This);
    private sealed record NotificationText(string Title, string Content);
}
