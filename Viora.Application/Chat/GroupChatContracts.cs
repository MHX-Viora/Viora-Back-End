using FluentValidation;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Application.Chat;

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
