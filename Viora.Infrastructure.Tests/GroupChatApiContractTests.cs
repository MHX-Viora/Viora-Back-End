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
        Assert.Equal((short)8, (short)MessageType.System);
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
        AssertProperties<GroupMemberListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<ChangeGroupPermissionResponse>("ConversationId", "CanSendMessage", "UpdatedAt");
        AssertProperties<RenameGroupResponse>("ConversationId", "Name", "UpdatedAt");
        AssertProperties<ChangeGroupAvatarResponse>("ConversationId", "AvatarUrl", "UpdatedAt");
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

    private static MethodInfo Method<T>(string name) => typeof(T).GetMethod(name) ?? throw new InvalidOperationException(name);
    private static void AssertRoute<T>(string method, string template) where T : HttpMethodAttribute => Assert.Equal(template, Method<ChatController>(method).GetCustomAttribute<T>()!.Template);
    private static void AssertProperties<T>(params string[] expected) => Assert.Equal(expected.Order(), typeof(T).GetProperties().Select(x => x.Name).Order());
}
