using Viora.Domain.Entities;

namespace Viora.Application.Notifications;

public static class NotificationFactory
{
    public static Notification Create(
        Guid recipientUserId,
        NotificationType notificationType,
        User? sender,
        Guid? referenceId,
        NotificationReferenceType? referenceType,
        PostType? postType = null,
        string? imageUrl = null)
    {
        var text = GetText(notificationType, sender?.DisplayName, postType);
        return new Notification
        {
            UserId = recipientUserId,
            SenderUserId = sender?.Id,
            NotificationType = notificationType,
            ReferenceId = referenceId,
            ReferenceType = referenceType,
            Title = text.Title,
            Content = text.Content,
            ImageUrl = imageUrl
        };
    }

    private static NotificationText GetText(
        NotificationType notificationType,
        string? senderName,
        PostType? postType)
    {
        var name = string.IsNullOrWhiteSpace(senderName) ? "Có người" : senderName;
        var words = GetPostWords(postType);

        return notificationType switch
        {
            NotificationType.System => new("Thông báo hệ thống", null),
            NotificationType.Follow => new("Theo dõi", $"{name} đã bắt đầu theo dõi bạn."),
            NotificationType.FriendRequest => new("Lời mời kết bạn", $"{name} đã gửi cho bạn lời mời kết bạn."),
            NotificationType.FriendAccepted => new("Kết bạn", $"{name} đã chấp nhận lời mời kết bạn của bạn."),
            NotificationType.FriendRejected => new("Kết bạn", $"{name} đã từ chối lời mời kết bạn của bạn."),
            NotificationType.PostLike => new("Cảm xúc", $"{name} đã bày tỏ cảm xúc với {words.Owner}."),
            NotificationType.PostComment => new("Bình luận", $"{name} đã bình luận {words.Owner}."),
            NotificationType.CommentReply => new("Phản hồi", $"{name} đã trả lời bình luận trong {words.Owner}."),
            NotificationType.Mention => new("Nhắc đến bạn", $"{name} đã nhắc đến bạn trong một {words.Singular}."),
            NotificationType.Message => new("Tin nhắn", $"{name} đã gửi cho bạn một tin nhắn mới."),
            NotificationType.GroupInvite or NotificationType.GroupRoleChanged or NotificationType.GroupRemoved => new("Nhóm chat", null),
            NotificationType.LiveStarted or NotificationType.LiveEnded => new("Livestream", null),
            NotificationType.IdentityApproved or NotificationType.IdentityRejected => new("Xác thực danh tính", null),
            NotificationType.AdminAnnouncement => new("Thông báo hệ thống", null),
            NotificationType.ShopNotification => new("Mua sắm", null),
            NotificationType.PostShare => new("Chia sẻ", $"{name} đã chia sẻ {words.Owner}."),
            _ => new("Thông báo", null)
        };
    }

    private static PostWords GetPostWords(PostType? postType) =>
        postType switch
        {
            PostType.ShortVideo => new("video", "video của bạn", "video này"),
            _ => new("bài viết", "bài viết của bạn", "bài viết này")
        };

    private sealed record PostWords(string Singular, string Owner, string This);
    private sealed record NotificationText(string Title, string? Content);
}
