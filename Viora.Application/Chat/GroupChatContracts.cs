using FluentValidation;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Application.Chat;

public static class GroupChatRoleMessages
{
    public const string OwnerMustTransferBeforeLeaving = "Chủ nhóm phải chuyển quyền sở hữu trước khi rời nhóm.";
    public const string PromotionNotification = "Bạn đã được bổ nhiệm làm quản trị viên.";
    public const string DemotionNotification = "Vai trò quản trị viên của bạn đã được gỡ.";
    public const string OwnerTransferNotification = "Bạn đã trở thành chủ nhóm.";

    public static string PromotionSystemMessage(string actorName, string targetName) => $"{actorName} đã cấp quyền quản trị viên cho {targetName}.";
    public static string DemotionSystemMessage(string actorName, string targetName) => $"{actorName} đã thu hồi quyền quản trị viên của {targetName}.";
    public static string OwnerTransferSystemMessage(string actorName, string targetName) => $"{actorName} đã chuyển quyền trưởng nhóm cho {targetName}.";
}

public static class GroupChatSystemMessages
{
    public static string Created(string actorName) => $"{actorName} đã tạo nhóm.";
    public static string Renamed(string actorName, string groupName) => $"{actorName} đã đổi tên nhóm thành \"{groupName}\".";
    public static string AvatarChanged(string actorName) => $"{actorName} đã đổi ảnh nhóm.";
    public static string MembersAdded(string actorName, IEnumerable<string> memberNames) => $"{actorName} đã thêm {string.Join(", ", memberNames)} vào nhóm.";
    public static string MemberRemoved(string actorName, string memberName) => $"{actorName} đã xóa {memberName} khỏi nhóm.";
    public static string MemberLeft(string memberName) => $"{memberName} đã rời khỏi nhóm.";
    public static string PermissionChanged(string actorName, ConversationSendPermission permission) => permission switch
    {
        ConversationSendPermission.Everyone => $"{actorName} đã tắt chế độ chỉ quản trị viên được gửi tin nhắn.",
        ConversationSendPermission.AdminsAndOwner => $"{actorName} đã bật chế độ chỉ quản trị viên được gửi tin nhắn.",
        ConversationSendPermission.OwnerOnly => $"{actorName} đã bật chế độ chỉ trưởng nhóm được gửi tin nhắn.",
        _ => throw new ArgumentOutOfRangeException(nameof(permission))
    };
}

public static class ChatMessagePolicy
{
    public static bool CanForward(MessageType messageType, bool isDeleted) =>
        !isDeleted && messageType is not (MessageType.System or MessageType.Recall);
    public static bool CanReply(MessageType messageType) => messageType != MessageType.System;
    public static bool CanEdit(MessageType messageType) => messageType != MessageType.System;
    public static bool CanRecall(MessageType messageType) => messageType != MessageType.System;
    public static bool CanReact(MessageType messageType) => messageType != MessageType.System;
}

public static class GroupChatRealtimeMessages
{
    public static ChatRealtimeMessageResponse CreateSystemMessage(
        Guid messageId,
        Guid conversationId,
        ChatMessageSenderResponse sender,
        string content,
        DateTime createdAt,
        bool isMine) =>
        new(
            messageId,
            conversationId,
            sender,
            MessageType.System,
            content,
            null,
            [],
            [],
            isMine,
            false,
            false,
            createdAt);
}

public enum GroupChatError { Validation, NotFound, Forbidden, Conflict }

public sealed record GroupChatResult<T>(bool IsSuccess, T? Value, GroupChatError? Error, string? Message)
{
    public static GroupChatResult<T> Success(T value) => new(true, value, null, null);
    public static GroupChatResult<T> Failure(GroupChatError error, string message) => new(false, default, error, message);
}

public sealed record SelectableFriendListResponse(int Page, int PageSize, int TotalItems, int TotalPages, IReadOnlyList<SelectableFriendResponse> Items);
public sealed record SelectableFriendResponse(Guid Id, string DisplayName, string? AvatarUrl, bool IsVerified, bool IsOnline, DateTime? LastActiveAt);

public sealed record CreateGroupCommand(Guid CurrentUserId, string Name, IReadOnlyList<Guid> MemberIds, CreatePostFile? Avatar);
public sealed record CreateGroupResponse(Guid Id, string Name, string? AvatarUrl, int MemberCount, DateTime CreatedAt);
public sealed record GroupUserResponse(Guid Id, string DisplayName, string? AvatarUrl);
public sealed record GroupMemberPreviewResponse(Guid Id, string DisplayName, string? AvatarUrl, ConversationMemberRole Role);
public sealed record GroupDetailsResponse(Guid Id, string Name, string? AvatarUrl, int MemberCount, ConversationMemberRole MyRole, ConversationSendPermission CanSendMessage, GroupUserResponse CreatedBy, IReadOnlyList<GroupMemberPreviewResponse> MembersPreview);
public sealed record GroupMemberListResponse(int Page, int PageSize, int TotalItems, int TotalPages, IReadOnlyList<GroupMemberResponse> Items);
public sealed record GroupMemberResponse(Guid Id, string DisplayName, string? AvatarUrl, bool IsVerified, ConversationMemberRole Role, bool IsOnline, DateTime JoinedAt);
public sealed record GroupMutationResponse(Guid ConversationId, string Action, DateTime UpdatedAt);
public sealed record ChangeGroupPermissionResponse(Guid ConversationId, ConversationSendPermission CanSendMessage, DateTime UpdatedAt);
public sealed record RenameGroupResponse(Guid ConversationId, string Name, DateTime UpdatedAt);
public sealed record ChangeGroupAvatarResponse(Guid ConversationId, string AvatarUrl, DateTime UpdatedAt);

public sealed record AddGroupMembersRequest(IReadOnlyList<Guid> MemberIds);
public sealed record RenameGroupRequest(string Name);
public sealed record ChangeGroupPermissionRequest(ConversationSendPermission CanSendMessage);
public sealed record TransferGroupOwnerRequest(Guid UserId);

public sealed class CreateGroupValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupValidator()
    {
        RuleFor(x => x.CurrentUserId).NotEmpty();
        RuleFor(x => x.Name).Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Tên nhóm không được rỗng.").MaximumLength(100);
        RuleFor(x => x.MemberIds).NotNull().Must(x => x.Count >= 2).WithMessage("Phải chọn ít nhất 2 thành viên.");
        RuleFor(x => x.MemberIds).Must(x => x.Distinct().Count() == x.Count).WithMessage("Danh sách thành viên không được trùng.");
        RuleFor(x => x).Must(x => !x.MemberIds.Contains(x.CurrentUserId)).WithMessage("Không được thêm chính mình vào memberIds.");
        When(x => x.Avatar is not null, () => RuleFor(x => x.Avatar!).Must(IsValidAvatar).WithMessage("Avatar phải là JPEG, PNG hoặc WebP và không vượt quá 5 MB."));
    }

    private static bool IsValidAvatar(CreatePostFile file) =>
        file.Length is > 0 and <= 5 * 1024 * 1024 &&
        file.ContentType is "image/jpeg" or "image/png" or "image/webp";
}

public interface IGroupChatService
{
    Task<SelectableFriendListResponse> GetSelectableFriendsAsync(Guid currentUserId, string? keyword, int page, int pageSize, CancellationToken token);
    Task<GroupChatResult<CreateGroupResponse>> CreateAsync(CreateGroupCommand command, CancellationToken token);
    Task<GroupChatResult<GroupDetailsResponse>> GetAsync(Guid currentUserId, Guid conversationId, CancellationToken token);
    Task<GroupChatResult<GroupMemberListResponse>> GetMembersAsync(Guid currentUserId, Guid conversationId, string? keyword, int page, int pageSize, CancellationToken token);
    Task<GroupChatResult<GroupMutationResponse>> AddMembersAsync(Guid actorId, Guid conversationId, IReadOnlyList<Guid> memberIds, CancellationToken token);
    Task<GroupChatResult<GroupMutationResponse>> RemoveMemberAsync(Guid actorId, Guid conversationId, Guid userId, CancellationToken token);
    Task<GroupChatResult<GroupMutationResponse>> LeaveAsync(Guid actorId, Guid conversationId, CancellationToken token);
    Task<GroupChatResult<RenameGroupResponse>> RenameAsync(Guid actorId, Guid conversationId, string name, CancellationToken token);
    Task<GroupChatResult<ChangeGroupAvatarResponse>> ChangeAvatarAsync(Guid actorId, Guid conversationId, CreatePostFile avatar, CancellationToken token);
    Task<GroupChatResult<ChangeGroupPermissionResponse>> ChangePermissionAsync(Guid actorId, Guid conversationId, ConversationSendPermission permission, CancellationToken token);
    Task<GroupChatResult<GroupMutationResponse>> SetAdminAsync(Guid actorId, Guid conversationId, Guid userId, bool isAdmin, CancellationToken token);
    Task<GroupChatResult<GroupMutationResponse>> TransferOwnerAsync(Guid actorId, Guid conversationId, Guid userId, CancellationToken token);
    Task<GroupChatResult<GroupMutationResponse>> DissolveAsync(Guid actorId, Guid conversationId, CancellationToken token);
}
