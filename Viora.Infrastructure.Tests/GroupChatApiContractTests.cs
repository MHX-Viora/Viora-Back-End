using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Chat;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Viora.Infrastructure.Persistence;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class GroupChatApiContractTests
{
    [Fact]
    public void Controllers_expose_authenticated_group_routes()
    {
        Assert.NotNull(typeof(ChatController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(typeof(FriendsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("selectable", Method<FriendsController>(nameof(FriendsController.Selectable)).GetCustomAttribute<HttpGetAttribute>()!.Template);

        AssertRoute<HttpPostAttribute>(nameof(ChatController.CreateGroup), "groups");
        AssertRoute<HttpGetAttribute>(nameof(ChatController.GetGroup), "groups/{conversationId:guid}");
        AssertRoute<HttpGetAttribute>(nameof(ChatController.PreviewGroup), "groups/preview/{conversationId:guid}");
        AssertRoute<HttpGetAttribute>(nameof(ChatController.PreviewGroupByInviteCode), "groups/preview");
        AssertRoute<HttpGetAttribute>(nameof(ChatController.GetGroupMembers), "groups/{conversationId:guid}/members");
        AssertRoute<HttpPostAttribute>(nameof(ChatController.AddGroupMembers), "groups/{conversationId:guid}/members");
        AssertRoute<HttpDeleteAttribute>(nameof(ChatController.RemoveGroupMember), "groups/{conversationId:guid}/members/{userId:guid}");
        AssertRoute<HttpPostAttribute>(nameof(ChatController.LeaveGroup), "groups/{conversationId:guid}/leave");
        AssertRoute<HttpPutAttribute>(nameof(ChatController.RenameGroup), "groups/{conversationId:guid}/name");
        AssertRoute<HttpPutAttribute>(nameof(ChatController.ChangeGroupAvatar), "groups/{conversationId:guid}/avatar");
        AssertRoute<HttpPutAttribute>(nameof(ChatController.ChangeGroupPermission), "groups/{conversationId:guid}/permission");
        AssertRoute<HttpPutAttribute>(nameof(ChatController.PromoteGroupAdmin), "groups/{conversationId:guid}/members/{userId:guid}/admin");
        AssertRoute<HttpDeleteAttribute>(nameof(ChatController.DemoteGroupAdmin), "groups/{conversationId:guid}/members/{userId:guid}/admin");
        AssertRoute<HttpPutAttribute>(nameof(ChatController.TransferGroupOwner), "groups/{conversationId:guid}/owner");
        AssertRoute<HttpDeleteAttribute>(nameof(ChatController.DissolveGroup), "groups/{conversationId:guid}");
    }

    [Fact]
    public async Task Create_validator_enforces_name_members_and_actor_rules()
    {
        var actor = Guid.NewGuid();
        var validator = new CreateGroupValidator();
        var duplicate = Guid.NewGuid();
        var result = await validator.ValidateAsync(new CreateGroupCommand(actor, " ", [actor, duplicate, duplicate], null));
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void Domain_enums_preserve_group_wire_values()
    {
        Assert.Equal((short)1, (short)ConversationType.Group);
        Assert.Equal((short)100, (short)MessageType.System);
        Assert.Equal((short)2, (short)ConversationMemberRole.Owner);
        Assert.Equal((short)2, (short)ConversationSendPermission.OwnerOnly);
    }

    [Fact]
    public async Task Public_message_validator_rejects_system_messages()
    {
        var validator = new SendChatMessageValidator();
        var result = await validator.ValidateAsync(new SendChatMessageCommand(Guid.NewGuid(), Guid.NewGuid(), null, MessageType.System, "forged", null));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Response_contracts_match_requested_shapes()
    {
        AssertProperties<SelectableFriendListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<SelectableFriendResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified", "IsOnline", "LastActiveAt");
        AssertProperties<CreateGroupResponse>("Id", "Name", "AvatarUrl", "MemberCount", "CreatedAt");
        AssertProperties<GroupDetailsResponse>("Id", "Name", "AvatarUrl", "MemberCount", "MyRole", "CanSendMessage", "CreatedBy", "MembersPreview");
        AssertProperties<GroupPreviewResponse>("Id", "Name", "AvatarUrl", "MemberCount", "IsJoined", "CreatedAt", "Members");
        AssertProperties<GroupPreviewMemberResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified", "IsFriend");
        AssertProperties<GroupMemberListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<ChangeGroupPermissionResponse>("ConversationId", "CanSendMessage", "UpdatedAt");
        AssertProperties<RenameGroupResponse>("ConversationId", "Name", "UpdatedAt");
        AssertProperties<ChangeGroupAvatarResponse>("ConversationId", "AvatarUrl", "UpdatedAt");
    }

    [Fact]
    public void Group_role_messages_are_fully_vietnamese()
    {
        var messages = new[]
        {
            GroupChatRoleMessages.OwnerMustTransferBeforeLeaving,
            GroupChatRoleMessages.PromotionNotification,
            GroupChatRoleMessages.DemotionNotification,
            GroupChatRoleMessages.PromotionSystemMessage("Quyền", "Nguyễn Văn A"),
            GroupChatRoleMessages.DemotionSystemMessage("Quyền", "Nguyễn Văn A"),
            GroupChatRoleMessages.OwnerTransferNotification,
            GroupChatRoleMessages.OwnerTransferSystemMessage("Quyền", "Nguyễn Văn A")
        };

        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("Admin", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Owner", message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void System_message_contract_uses_actor_names_and_is_immutable()
    {
        Assert.Equal("Quyền đã tạo nhóm.", GroupChatSystemMessages.Created("Quyền"));
        Assert.Equal("Quyền đã đổi tên nhóm thành \"Du lịch Đà Lạt\".", GroupChatSystemMessages.Renamed("Quyền", "Du lịch Đà Lạt"));
        Assert.Equal("Quyền đã thêm Nam vào nhóm.", GroupChatSystemMessages.MembersAdded("Quyền", ["Nam"]));
        Assert.Equal("Quyền đã xóa Nam khỏi nhóm.", GroupChatSystemMessages.MemberRemoved("Quyền", "Nam"));
        Assert.Equal("Nam đã rời khỏi nhóm.", GroupChatSystemMessages.MemberLeft("Nam"));
        Assert.Equal("Quyền đã bật chế độ chỉ quản trị viên được gửi tin nhắn.", GroupChatSystemMessages.PermissionChanged("Quyền", ConversationSendPermission.AdminsAndOwner));
        Assert.False(ChatMessagePolicy.CanReply(MessageType.System));
        Assert.False(ChatMessagePolicy.CanEdit(MessageType.System));
        Assert.False(ChatMessagePolicy.CanRecall(MessageType.System));
        Assert.False(ChatMessagePolicy.CanReact(MessageType.System));
    }

    [Fact]
    public void Group_system_realtime_payload_matches_normal_message_shape()
    {
        var messageId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var sender = new ChatMessageSenderResponse(senderId, "Quyền", null, false);

        var payload = GroupChatRealtimeMessages.CreateSystemMessage(
            messageId,
            conversationId,
            sender,
            "Quyền đã đổi tên nhóm.",
            createdAt,
            true);

        Assert.Equal(MessageType.System, payload.MessageType);
        Assert.Equal("Quyền đã đổi tên nhóm.", payload.Content);
        Assert.True(payload.IsMine);
        Assert.Empty(payload.Attachments);
        Assert.Empty(payload.Reactions);
        Assert.Null(payload.Reply);
    }

    [Fact]
    public void Selectable_friends_query_can_be_translated_by_postgresql_provider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation_only;Username=test;Password=test")
            .Options;
        using var db = new AppDbContext(options);
        var userId = Guid.NewGuid();

        var query = db.Friendships
            .Where(x => x.Status == FriendshipStatus.Accepted &&
                (x.RequesterUserId == userId || x.AddresseeUserId == userId))
            .Select(x => new
            {
                Id = x.RequesterUserId == userId ? x.AddresseeUserId : x.RequesterUserId,
                DisplayName = x.RequesterUserId == userId ? x.AddresseeUser.DisplayName : x.RequesterUser.DisplayName,
                AccountStatus = x.RequesterUserId == userId ? x.AddresseeUser.Account.Status : x.RequesterUser.Account.Status,
                AccountDeletedAt = x.RequesterUserId == userId ? x.AddresseeUser.Account.DeletedAt : x.RequesterUser.Account.DeletedAt
            })
            .Where(x => x.AccountStatus == AccountStatus.Active && x.AccountDeletedAt == null);

        var sql = query.ToQueryString();
        Assert.Contains("SELECT", sql);
    }

    [Fact]
    public void Group_preview_query_can_be_translated_by_postgresql_provider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation_only;Username=test;Password=test")
            .Options;
        using var db = new AppDbContext(options);
        var actorId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var query = db.ConversationMembers.AsNoTracking()
            .Where(x => x.ConversationId == groupId && x.Status == ConversationMemberStatus.Active)
            .Select(x => new
            {
                x.UserId,
                x.User.DisplayName,
                x.User.AvatarUrl,
                x.User.IsVerified,
                x.Role,
                x.JoinedAt,
                IsFriend = db.Friendships.Any(f =>
                    f.Status == FriendshipStatus.Accepted &&
                    ((f.RequesterUserId == actorId && f.AddresseeUserId == x.UserId) ||
                     (f.AddresseeUserId == actorId && f.RequesterUserId == x.UserId)))
            })
            .OrderByDescending(x => x.IsFriend)
            .ThenByDescending(x => x.Role == ConversationMemberRole.Owner)
            .ThenByDescending(x => x.Role == ConversationMemberRole.Admin)
            .ThenBy(x => x.JoinedAt)
            .ThenBy(x => x.UserId)
            .Take(5)
            .Select(x => new GroupPreviewMemberResponse(x.UserId, x.DisplayName, x.AvatarUrl, x.IsVerified, x.IsFriend));

        var sql = query.ToQueryString();
        Assert.Contains("EXISTS", sql);
        Assert.Contains("LIMIT", sql);
    }

    private static MethodInfo Method<T>(string name) => typeof(T).GetMethod(name) ?? throw new InvalidOperationException(name);
    private static void AssertRoute<T>(string method, string template) where T : HttpMethodAttribute => Assert.Equal(template, Method<ChatController>(method).GetCustomAttribute<T>()!.Template);
    private static void AssertProperties<T>(params string[] expected) => Assert.Equal(expected.Order(), typeof(T).GetProperties().Select(x => x.Name).Order());
}
