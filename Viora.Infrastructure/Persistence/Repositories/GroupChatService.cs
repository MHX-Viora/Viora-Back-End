using Microsoft.EntityFrameworkCore;
using Viora.Application.Chat;
using Viora.Application.Notifications;
using Viora.Application.Posts;
using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class GroupChatService(
    AppDbContext db,
    IMediaStorage mediaStorage,
    IOnlineUserRegistry onlineUsers,
    IRealtimeService realtime,
    INotificationService notificationService) : IGroupChatService
{
    public async Task<SelectableFriendListResponse> GetSelectableFriendsAsync(Guid userId, string? keyword, int page, int pageSize, CancellationToken token)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Friendships.AsNoTracking()
            .Where(x => x.Status == FriendshipStatus.Accepted && (x.RequesterUserId == userId || x.AddresseeUserId == userId))
            .Select(x => new
            {
                Id = x.RequesterUserId == userId ? x.AddresseeUserId : x.RequesterUserId,
                DisplayName = x.RequesterUserId == userId ? x.AddresseeUser.DisplayName : x.RequesterUser.DisplayName,
                AvatarUrl = x.RequesterUserId == userId ? x.AddresseeUser.AvatarUrl : x.RequesterUser.AvatarUrl,
                IsVerified = x.RequesterUserId == userId ? x.AddresseeUser.IsVerified : x.RequesterUser.IsVerified,
                AccountStatus = x.RequesterUserId == userId ? x.AddresseeUser.Account.Status : x.RequesterUser.Account.Status,
                AccountDeletedAt = x.RequesterUserId == userId ? x.AddresseeUser.Account.DeletedAt : x.RequesterUser.Account.DeletedAt,
                LastActiveAt = x.RequesterUserId == userId ? x.AddresseeUser.Account.LastLoginAt : x.RequesterUser.Account.LastLoginAt
            })
            .Where(x => x.AccountStatus == AccountStatus.Active && x.AccountDeletedAt == null);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.DisplayName, pattern));
        }

        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).ToListAsync(token);
        var items = rows.Select(x => new SelectableFriendResponse(x.Id, x.DisplayName, x.AvatarUrl, x.IsVerified, onlineUsers.IsOnline(x.Id), x.LastActiveAt))
            .OrderByDescending(x => x.IsOnline).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new(page, pageSize, total, Pages(total, pageSize), items);
    }

    public async Task<GroupChatResult<CreateGroupResponse>> CreateAsync(CreateGroupCommand command, CancellationToken token)
    {
        var validator = new CreateGroupValidator();
        var validation = await validator.ValidateAsync(command, token);
        if (!validation.IsValid) return Fail<CreateGroupResponse>(GroupChatError.Validation, validation.Errors[0].ErrorMessage);

        var ids = command.MemberIds.ToArray();
        var validUsers = await db.Users.Where(x => ids.Contains(x.Id) && x.Account.Status == AccountStatus.Active && x.Account.DeletedAt == null).Select(x => x.Id).ToListAsync(token);
        if (validUsers.Count != ids.Length) return Fail<CreateGroupResponse>(GroupChatError.NotFound, "Một hoặc nhiều người dùng không tồn tại hoặc đã bị khóa.");
        var friendIds = await AcceptedFriendIds(command.CurrentUserId, ids, token);
        if (friendIds.Count != ids.Length) return Fail<CreateGroupResponse>(GroupChatError.Forbidden, "Chỉ có thể thêm bạn bè đã được chấp nhận vào nhóm.");

        string? avatarUrl = null;
        if (command.Avatar is not null) avatarUrl = (await mediaStorage.UploadGroupAvatarAsync(command.CurrentUserId, command.Avatar, token)).MediaUrl;
        var actor = await db.Users.SingleOrDefaultAsync(x => x.Id == command.CurrentUserId && x.Account.Status == AccountStatus.Active && x.Account.DeletedAt == null, token);
        if (actor is null) return Fail<CreateGroupResponse>(GroupChatError.Forbidden, "Tài khoản không còn hoạt động.");
        var now = DateTime.UtcNow;
        var group = new Conversation { ConversationType = ConversationType.Group, Name = command.Name.Trim(), AvatarUrl = avatarUrl, CreatedBy = command.CurrentUserId, CanSendMessage = ConversationSendPermission.Everyone, CreatedAt = now, UpdatedAt = now };
        group.Members.Add(Member(group, command.CurrentUserId, ConversationMemberRole.Owner, command.CurrentUserId, now));
        foreach (var id in ids) group.Members.Add(Member(group, id, ConversationMemberRole.Member, command.CurrentUserId, now));
        var message = SystemMessage(group, command.CurrentUserId, $"{actor.DisplayName} đã tạo nhóm {group.Name}.", now);
        group.LastMessageId = message.Id; group.LastMessageAt = now;
        var notifications = ids.Select(id => GroupNotification(id, command.CurrentUserId, NotificationType.GroupInvite, group.Id, $"{actor.DisplayName} đã thêm bạn vào nhóm {group.Name}.", now)).ToList();
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            db.Conversations.Add(group); db.Messages.Add(message); db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        }
        var response = new CreateGroupResponse(group.Id, group.Name, group.AvatarUrl, ids.Length + 1, group.CreatedAt);
        await Publish(ids.Append(command.CurrentUserId), RealtimeEvents.ConversationCreated, response, notifications, token);
        return GroupChatResult<CreateGroupResponse>.Success(response);
    }

    public async Task<GroupChatResult<GroupDetailsResponse>> GetAsync(Guid actorId, Guid conversationId, CancellationToken token)
    {
        var group = await ActiveGroup(conversationId, token);
        var me = group?.Members.FirstOrDefault(x => x.UserId == actorId && x.Status == ConversationMemberStatus.Active);
        if (group is null) return Fail<GroupDetailsResponse>(GroupChatError.NotFound, "Không tìm thấy nhóm.");
        if (me is null) return Fail<GroupDetailsResponse>(GroupChatError.Forbidden, "Bạn không phải thành viên của nhóm.");
        var creator = await db.Users.AsNoTracking().Where(x => x.Id == group.CreatedBy).Select(x => new GroupUserResponse(x.Id, x.DisplayName, x.AvatarUrl)).SingleAsync(token);
        var active = group.Members.Where(x => x.Status == ConversationMemberStatus.Active).ToList();
        var preview = active.OrderByDescending(x => x.Role).ThenBy(x => x.User.DisplayName).Take(5).Select(x => new GroupMemberPreviewResponse(x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.Role)).ToList();
        return GroupChatResult<GroupDetailsResponse>.Success(new(group.Id, group.Name!, group.AvatarUrl, active.Count, me.Role, group.CanSendMessage, creator, preview));
    }

    public async Task<GroupChatResult<GroupMemberListResponse>> GetMembersAsync(Guid actorId, Guid conversationId, string? keyword, int page, int pageSize, CancellationToken token)
    {
        if (!await IsActiveMember(actorId, conversationId, token)) return Fail<GroupMemberListResponse>(GroupChatError.Forbidden, "Bạn không phải thành viên của nhóm.");
        if (!await IsActiveGroup(conversationId, token)) return Fail<GroupMemberListResponse>(GroupChatError.NotFound, "Không tìm thấy nhóm.");
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.ConversationMembers.AsNoTracking().Where(x => x.ConversationId == conversationId && x.Status == ConversationMemberStatus.Active);
        if (!string.IsNullOrWhiteSpace(keyword)) { var pattern = $"%{keyword.Trim()}%"; query = query.Where(x => EF.Functions.ILike(x.User.DisplayName, pattern)); }
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(x => x.Role).ThenBy(x => x.User.DisplayName).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.UserId, x.User.DisplayName, x.User.AvatarUrl, x.User.IsVerified, x.Role, x.JoinedAt }).ToListAsync(token);
        var items = rows.Select(x => new GroupMemberResponse(x.UserId, x.DisplayName, x.AvatarUrl, x.IsVerified, x.Role, onlineUsers.IsOnline(x.UserId), x.JoinedAt)).ToList();
        return GroupChatResult<GroupMemberListResponse>.Success(new(page, pageSize, total, Pages(total, pageSize), items));
    }

    public async Task<GroupChatResult<GroupMutationResponse>> AddMembersAsync(Guid actorId, Guid id, IReadOnlyList<Guid> memberIds, CancellationToken token)
    {
        var ids = memberIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Length != memberIds.Count || ids.Contains(actorId)) return FailMutation(GroupChatError.Validation, "Danh sách thành viên không hợp lệ.");
        var access = await Access(actorId, id, token);
        if (access.Group is null) return Missing();
        if (access.Member?.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin)) return Forbidden();
        var valid = await db.Users.Where(x => ids.Contains(x.Id) && x.Account.Status == AccountStatus.Active && x.Account.DeletedAt == null).Select(x => x.Id).ToListAsync(token);
        if (valid.Count != ids.Length) return FailMutation(GroupChatError.NotFound, "Một hoặc nhiều người dùng không tồn tại hoặc đã bị khóa.");
        if (access.Group.Members.Any(x => ids.Contains(x.UserId) && x.Status == ConversationMemberStatus.Active)) return FailMutation(GroupChatError.Conflict, "Có người dùng đã ở trong nhóm.");
        var now = DateTime.UtcNow; var names = await db.Users.Where(x => ids.Contains(x.Id)).Select(x => x.DisplayName).ToListAsync(token);
        foreach (var userId in ids)
        {
            var old = access.Group.Members.SingleOrDefault(x => x.UserId == userId);
            if (old is null) access.Group.Members.Add(Member(access.Group, userId, ConversationMemberRole.Member, actorId, now));
            else { old.Status = ConversationMemberStatus.Active; old.Role = ConversationMemberRole.Member; old.JoinedAt = now; old.JoinedBy = actorId; }
        }
        var notifications = ids.Select(x => GroupNotification(x, actorId, NotificationType.GroupInvite, id, "Bạn đã được thêm vào một nhóm chat.", now)).ToList();
        return await SaveMutation(access.Group, actorId, $"Đã thêm {string.Join(", ", names)} vào nhóm.", "members-added", RealtimeEvents.MemberAdded, ids, notifications, token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> RemoveMemberAsync(Guid actorId, Guid id, Guid userId, CancellationToken token)
    {
        if (actorId == userId) return FailMutation(GroupChatError.Validation, "Hãy dùng API rời nhóm.");
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member is null) return Forbidden();
        var target = access.Group.Members.SingleOrDefault(x => x.UserId == userId && x.Status == ConversationMemberStatus.Active);
        if (target is null) return FailMutation(GroupChatError.NotFound, "Không tìm thấy thành viên.");
        var allowed = access.Member.Role == ConversationMemberRole.Owner || access.Member.Role == ConversationMemberRole.Admin && target.Role == ConversationMemberRole.Member;
        if (!allowed) return Forbidden();
        target.Status = ConversationMemberStatus.Kicked;
        var notice = GroupNotification(userId, actorId, NotificationType.GroupRemoved, id, "Bạn đã bị xóa khỏi nhóm chat.", DateTime.UtcNow);
        return await SaveMutation(access.Group, actorId, $"{target.User.DisplayName} đã bị xóa khỏi nhóm.", "member-removed", RealtimeEvents.MemberRemoved, [userId], [notice], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> LeaveAsync(Guid actorId, Guid id, CancellationToken token)
    {
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member is null) return Forbidden();
        var others = access.Group.Members.Count(x => x.Status == ConversationMemberStatus.Active && x.UserId != actorId);
        if (access.Member.Role == ConversationMemberRole.Owner && others > 0) return FailMutation(GroupChatError.Conflict, "Owner phải chuyển quyền sở hữu trước khi rời nhóm.");
        access.Member.Status = ConversationMemberStatus.Left;
        if (others == 0) access.Group.DeletedAt = DateTime.UtcNow;
        return await SaveMutation(access.Group, actorId, $"{access.Member.User.DisplayName} đã rời nhóm.", "left", RealtimeEvents.MemberLeft, [actorId], [], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> RenameAsync(Guid actorId, Guid id, string name, CancellationToken token)
    {
        name = name?.Trim() ?? ""; if (name.Length is 0 or > 100) return FailMutation(GroupChatError.Validation, "Tên nhóm phải từ 1 đến 100 ký tự.");
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member?.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin)) return Forbidden();
        access.Group.Name = name; return await SaveMutation(access.Group, actorId, $"Đã đổi tên nhóm thành {name}.", "renamed", RealtimeEvents.ConversationRenamed, [], [], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> ChangeAvatarAsync(Guid actorId, Guid id, CreatePostFile avatar, CancellationToken token)
    {
        if (avatar.Length is <= 0 or > 5 * 1024 * 1024 || avatar.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return FailMutation(GroupChatError.Validation, "Avatar không hợp lệ.");
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member?.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin)) return Forbidden();
        access.Group.AvatarUrl = (await mediaStorage.UploadGroupAvatarAsync(actorId, avatar, token)).MediaUrl;
        return await SaveMutation(access.Group, actorId, "Đã đổi avatar nhóm.", "avatar-changed", RealtimeEvents.ConversationAvatarChanged, [], [], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> ChangePermissionAsync(Guid actorId, Guid id, ConversationSendPermission permission, CancellationToken token)
    {
        if (!Enum.IsDefined(permission)) return FailMutation(GroupChatError.Validation, "Quyền gửi tin nhắn không hợp lệ.");
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member?.Role != ConversationMemberRole.Owner) return Forbidden();
        access.Group.CanSendMessage = permission; return await SaveMutation(access.Group, actorId, "Đã thay đổi quyền gửi tin nhắn.", "permission-changed", RealtimeEvents.ConversationUpdated, [], [], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> SetAdminAsync(Guid actorId, Guid id, Guid userId, bool isAdmin, CancellationToken token)
    {
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member?.Role != ConversationMemberRole.Owner) return Forbidden();
        var target = access.Group.Members.SingleOrDefault(x => x.UserId == userId && x.Status == ConversationMemberStatus.Active);
        var expected = isAdmin ? ConversationMemberRole.Member : ConversationMemberRole.Admin;
        if (target is null) return FailMutation(GroupChatError.NotFound, "Không tìm thấy thành viên.");
        if (target.Role != expected) return FailMutation(GroupChatError.Conflict, "Vai trò hiện tại không phù hợp.");
        target.Role = isAdmin ? ConversationMemberRole.Admin : ConversationMemberRole.Member;
        var notice = GroupNotification(userId, actorId, NotificationType.GroupRoleChanged, id, isAdmin ? "Bạn đã được bổ nhiệm làm Admin." : "Vai trò Admin của bạn đã được gỡ.", DateTime.UtcNow);
        return await SaveMutation(access.Group, actorId, isAdmin ? $"{target.User.DisplayName} đã trở thành Admin." : $"{target.User.DisplayName} không còn là Admin.", isAdmin ? "admin-promoted" : "admin-demoted", RealtimeEvents.ConversationUpdated, [userId], [notice], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> TransferOwnerAsync(Guid actorId, Guid id, Guid userId, CancellationToken token)
    {
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member?.Role != ConversationMemberRole.Owner) return Forbidden();
        var target = access.Group.Members.SingleOrDefault(x => x.UserId == userId && x.Status == ConversationMemberStatus.Active && x.Role != ConversationMemberRole.Owner);
        if (target is null) return FailMutation(GroupChatError.NotFound, "Không tìm thấy thành viên nhận quyền.");
        access.Member.Role = ConversationMemberRole.Admin; target.Role = ConversationMemberRole.Owner;
        var notice = GroupNotification(userId, actorId, NotificationType.GroupRoleChanged, id, "Bạn đã trở thành Owner của nhóm.", DateTime.UtcNow);
        return await SaveMutation(access.Group, actorId, $"Đã chuyển quyền Owner cho {target.User.DisplayName}.", "owner-transferred", RealtimeEvents.ConversationUpdated, [userId], [notice], token);
    }

    public async Task<GroupChatResult<GroupMutationResponse>> DissolveAsync(Guid actorId, Guid id, CancellationToken token)
    {
        var access = await Access(actorId, id, token); if (access.Group is null) return Missing(); if (access.Member?.Role != ConversationMemberRole.Owner) return Forbidden();
        var recipients = access.Group.Members.Where(x => x.Status == ConversationMemberStatus.Active).Select(x => x.UserId).ToArray();
        foreach (var member in access.Group.Members.Where(x => x.Status == ConversationMemberStatus.Active)) member.Status = ConversationMemberStatus.Kicked;
        access.Group.DeletedAt = DateTime.UtcNow;
        return await SaveMutation(access.Group, actorId, "Nhóm đã được giải tán.", "dissolved", RealtimeEvents.ConversationUpdated, recipients, [], token);
    }

    private async Task<GroupChatResult<GroupMutationResponse>> SaveMutation(Conversation group, Guid actorId, string content, string action, string eventName, IEnumerable<Guid> directRecipients, IReadOnlyList<Notification> notifications, CancellationToken token)
    {
        var now = DateTime.UtcNow; var message = SystemMessage(group, actorId, content, now); group.LastMessageId = message.Id; group.LastMessageAt = now; group.UpdatedAt = now;
        var members = group.Members.Where(x => x.Status == ConversationMemberStatus.Active).Select(x => x.UserId).Concat(directRecipients).Distinct().ToArray();
        await using (var tx = await db.Database.BeginTransactionAsync(token)) { db.Messages.Add(message); db.Notifications.AddRange(notifications); await db.SaveChangesAsync(token); await tx.CommitAsync(token); }
        var response = new GroupMutationResponse(group.Id, action, group.UpdatedAt);
        await Publish(members, eventName, new { response, systemMessage = content }, notifications, token);
        return GroupChatResult<GroupMutationResponse>.Success(response);
    }

    private async Task Publish(IEnumerable<Guid> users, string eventName, object payload, IReadOnlyList<Notification> notifications, CancellationToken token)
    {
        await realtime.SendToUsersAsync(users, eventName, payload, token);
        await realtime.SendToUsersAsync(users, RealtimeEvents.ReceiveMessage, payload, token);
        foreach (var notification in notifications) await notificationService.PublishAsync(notification, token);
    }

    private async Task<(Conversation? Group, ConversationMember? Member)> Access(Guid actorId, Guid id, CancellationToken token)
    {
        var group = await ActiveGroup(id, token); return (group, group?.Members.SingleOrDefault(x => x.UserId == actorId && x.Status == ConversationMemberStatus.Active));
    }
    private Task<Conversation?> ActiveGroup(Guid id, CancellationToken token) => db.Conversations.Include(x => x.Members).ThenInclude(x => x.User).SingleOrDefaultAsync(x => x.Id == id && x.ConversationType == ConversationType.Group && x.DeletedAt == null, token);
    private Task<bool> IsActiveGroup(Guid id, CancellationToken token) => db.Conversations.AnyAsync(x => x.Id == id && x.ConversationType == ConversationType.Group && x.DeletedAt == null, token);
    private Task<bool> IsActiveMember(Guid userId, Guid id, CancellationToken token) => db.ConversationMembers.AnyAsync(x => x.ConversationId == id && x.UserId == userId && x.Status == ConversationMemberStatus.Active, token);
    private async Task<HashSet<Guid>> AcceptedFriendIds(Guid actorId, Guid[] ids, CancellationToken token) => (await db.Friendships.Where(x => x.Status == FriendshipStatus.Accepted && ((x.RequesterUserId == actorId && ids.Contains(x.AddresseeUserId)) || (x.AddresseeUserId == actorId && ids.Contains(x.RequesterUserId)))).Select(x => x.RequesterUserId == actorId ? x.AddresseeUserId : x.RequesterUserId).ToListAsync(token)).ToHashSet();
    private static ConversationMember Member(Conversation group, Guid userId, ConversationMemberRole role, Guid joinedBy, DateTime now) => new() { Conversation = group, UserId = userId, Role = role, Status = ConversationMemberStatus.Active, JoinedBy = joinedBy, JoinedAt = now };
    private static Message SystemMessage(Conversation group, Guid actorId, string content, DateTime now) => new() { Id = Guid.NewGuid(), Conversation = group, ConversationId = group.Id, SenderUserId = actorId, MessageType = MessageType.System, Content = content, CreatedAt = now, UpdatedAt = now };
    private static Notification GroupNotification(Guid userId, Guid actorId, NotificationType type, Guid groupId, string content, DateTime now) => new() { Id = Guid.NewGuid(), UserId = userId, SenderUserId = actorId, NotificationType = type, ReferenceId = groupId, ReferenceType = NotificationReferenceType.Conversation, Title = "Nhóm chat", Content = content, CreatedAt = now };
    private static int Pages(int total, int size) => total == 0 ? 0 : (int)Math.Ceiling(total / (double)size);
    private static GroupChatResult<T> Fail<T>(GroupChatError error, string message) => GroupChatResult<T>.Failure(error, message);
    private static GroupChatResult<GroupMutationResponse> FailMutation(GroupChatError error, string message) => Fail<GroupMutationResponse>(error, message);
    private static GroupChatResult<GroupMutationResponse> Missing() => FailMutation(GroupChatError.NotFound, "Không tìm thấy nhóm.");
    private static GroupChatResult<GroupMutationResponse> Forbidden() => FailMutation(GroupChatError.Forbidden, "Bạn không có quyền thực hiện thao tác này.");
}
