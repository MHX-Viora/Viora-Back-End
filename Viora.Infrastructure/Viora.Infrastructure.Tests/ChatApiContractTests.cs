using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Viora.Application.Chat;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ChatApiContractTests
{
    [Fact]
    public void Dissolved_conversation_has_distinct_error_and_realtime_payload()
    {
        Assert.True(Enum.IsDefined(ChatError.ConversationDissolved));
        AssertProperties<ConversationDissolvedPayload>("ConversationId");
        Assert.Equal("ConversationDissolved", RealtimeEvents.ConversationDissolved);
        Assert.Contains(
            typeof(IRealtimeService).GetMethods(),
            method => method.Name == "RemoveUsersFromGroupAsync");
    }
    [Fact]
    public void Chat_controller_exposes_authenticated_conversations_route()
    {
        Assert.NotNull(typeof(ChatController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/chat", typeof(ChatController).GetCustomAttribute<RouteAttribute>()!.Template);

        var action = typeof(ChatController).GetMethod(nameof(ChatController.Conversations))!;
        Assert.Equal("conversations", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void Chat_controller_exposes_unread_summary_route()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.UnreadSummary))!;

        Assert.Equal("unread-summary", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(ChatUnreadSummaryResponse));
        AssertProperties<ChatUnreadSummaryResponse>("TotalUnreadCount");
    }

    [Fact]
    public void Chat_controller_exposes_create_private_conversation_route()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.CreatePrivateConversation))!;

        Assert.Equal("conversations/private", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        var responses = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        Assert.Contains(responses, value => value.StatusCode == StatusCodes.Status200OK && value.Type == typeof(CreatePrivateConversationResponse));
        Assert.Contains(responses, value => value.StatusCode == StatusCodes.Status201Created && value.Type == typeof(CreatePrivateConversationResponse));
        Assert.Contains(responses, value => value.StatusCode == StatusCodes.Status400BadRequest && value.Type == typeof(ProblemDetails));
        Assert.Contains(responses, value => value.StatusCode == StatusCodes.Status403Forbidden && value.Type == typeof(ProblemDetails));
        Assert.Contains(responses, value => value.StatusCode == StatusCodes.Status404NotFound && value.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Create_private_conversation_contracts_have_expected_fields()
    {
        AssertProperties<CreatePrivateConversationRequest>("UserId");
        AssertProperties<CreatePrivateConversationResponse>("ConversationId", "IsCreated");
    }

    [Fact]
    public void Chat_conversations_query_parameters_match_contract()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.Conversations))!;
        var parameters = action.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.Equal(typeof(int), parameters["page"].ParameterType);
        Assert.Equal(1, parameters["page"].DefaultValue);
        Assert.Equal(typeof(int), parameters["pageSize"].ParameterType);
        Assert.Equal(20, parameters["pageSize"].DefaultValue);
        Assert.Equal(typeof(string), parameters["keyword"].ParameterType);
    }

    [Fact]
    public void Chat_messages_query_parameters_match_contract()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.Messages))!;
        var parameters = action.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.Equal(typeof(Guid), parameters["conversationId"].ParameterType);
        Assert.Equal(typeof(int), parameters["page"].ParameterType);
        Assert.Equal(1, parameters["page"].DefaultValue);
        Assert.Equal(typeof(int), parameters["pageSize"].ParameterType);
        Assert.Equal(30, parameters["pageSize"].DefaultValue);
    }

    [Fact]
    public void Send_chat_message_request_contract_has_expected_fields()
    {
        AssertProperties<SendChatMessageRequest>(
            "ConversationId",
            "ReplyMessageId",
            "MessageType",
            "Content",
            "Attachments");
        Assert.DoesNotContain(
            typeof(SendChatMessageRequest).GetProperties(),
            property => property.Name.Contains("Sender", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Forward_message_contract_matches_route_and_response()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.ForwardMessage))!;
        Assert.Equal("messages/{messageId:guid}/forward", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        AssertProperties<ForwardChatMessageRequest>("ConversationIds");
        AssertProperties<ForwardChatMessageResponse>("Success");
    }

    [Fact]
    public async Task Forward_message_validator_rejects_empty_duplicate_or_oversized_targets()
    {
        var validator = new ForwardChatMessageValidator();
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var duplicate = Guid.NewGuid();

        Assert.False((await validator.ValidateAsync(new ForwardChatMessageCommand(userId, messageId, null!))).IsValid);
        Assert.False((await validator.ValidateAsync(new ForwardChatMessageCommand(userId, messageId, []))).IsValid);
        Assert.False((await validator.ValidateAsync(new ForwardChatMessageCommand(userId, messageId, [duplicate, duplicate]))).IsValid);
        Assert.False((await validator.ValidateAsync(new ForwardChatMessageCommand(userId, messageId, Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToList()))).IsValid);
    }

    [Fact]
    public void Forward_message_policy_rejects_system_recalled_and_deleted_messages()
    {
        Assert.False(ChatMessagePolicy.CanForward(MessageType.System, false));
        Assert.False(ChatMessagePolicy.CanForward(MessageType.Recall, false));
        Assert.False(ChatMessagePolicy.CanForward(MessageType.Text, true));
        Assert.True(ChatMessagePolicy.CanForward(MessageType.Text, false));
    }

    [Fact]
    public void Chat_attachment_upload_request_contract_has_expected_fields()
    {
        AssertProperties<ChatAttachmentUploadRequest>("Files");
        AssertProperties<ChatAttachmentUploadResponse>(
            "FileUrl",
            "FileName",
            "MimeType",
            "ThumbnailUrl",
            "Duration",
            "FileSize");
    }

    [Fact]
    public void Mark_conversation_read_has_no_body_and_uses_route_conversation_id()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.MarkRead))!;
        var parameters = action.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.Equal(typeof(Guid), parameters["conversationId"].ParameterType);
        Assert.DoesNotContain(parameters.Keys, name => name.Contains("user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Set_conversation_pin_request_contract_has_expected_fields()
    {
        AssertProperties<SetConversationPinRequest>("IsPinned");
        AssertProperties<SetConversationPinResponse>("ConversationId", "IsPinned");
        AssertProperties<ConversationPinnedChangedPayload>("ConversationId", "IsPinned");
    }

    [Fact]
    public void Chat_settings_contracts_have_expected_fields()
    {
        AssertProperties<SetConversationMuteRequest>("IsMuted");
        AssertProperties<SetConversationMuteResponse>("ConversationId", "IsMuted");
        AssertProperties<ConversationMutedChangedPayload>("ConversationId", "IsMuted");
        AssertProperties<SetConversationBlockRequest>("IsBlocked");
        AssertProperties<SetConversationBlockResponse>("ConversationId", "IsBlocked");
        AssertProperties<ConversationBlockedChangedPayload>("ConversationId", "IsBlocked");
        AssertProperties<ChatConversationInfoResponse>(
            "Id",
            "ConversationType",
            "Name",
            "AvatarUrl",
            "MemberCount",
            "IsPinned",
            "IsMuted",
            "IsBlocked",
            "CanSendMessage",
            "CreatedBy",
            "OtherUser");
        AssertProperties<ChatConversationOtherUserResponse>(
            "Id",
            "DisplayName",
            "AvatarUrl",
            "IsVerified");
        AssertProperties<ChatAttachmentListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<ChatAttachmentListItemResponse>(
            "MessageId",
            "AttachmentId",
            "FileUrl",
            "FileName",
            "MimeType",
            "FileSize",
            "Duration",
            "CreatedAt");
        AssertProperties<ChatLinkListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<ChatLinkItemResponse>("MessageId", "Url", "Sender", "CreatedAt");
        AssertProperties<ChatLinkSenderResponse>("Id", "DisplayName");
        AssertProperties<ChatMessageSearchResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<ChatMessageSearchItemResponse>("MessageId", "Content", "Sender", "CreatedAt");
    }

    [Fact]
    public void Chat_conversation_response_contract_has_expected_fields()
    {
        AssertProperties<ChatConversationListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<ChatConversationItemResponse>(
            "Id",
            "ConversationType",
            "Name",
            "AvatarUrl",
            "OtherParticipant",
            "MemberCount",
            "LastMessage",
            "UnreadCount",
            "IsMuted",
            "IsPinned",
            "LastMessageAt",
            "UpdatedAt");
        AssertProperties<ChatLastMessageResponse>(
            "Id",
            "SenderId",
            "SenderName",
            "MessageType",
            "Content",
            "Attachments",
            "CreatedAt",
            "IsMine");
    }

    [Fact]
    public void Chat_message_response_contract_has_expected_fields()
    {
        AssertProperties<ChatMessageListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Conversation", "Items");
        AssertProperties<ChatMessageConversationResponse>(
            "Id",
            "Type",
            "IsBlocked",
            "BlockedBy",
            "OnlyAdminCanSend",
            "CanSendMessage");
        AssertProperties<ChatMessageItemResponse>(
            "Id",
            "Sender",
            "MessageType",
            "Content",
            "ReplyMessage",
            "Attachments",
            "Reactions",
            "ReactionSummary",
            "IsMine",
            "IsEdited",
            "IsDeleted",
            "CreatedAt",
            "UpdatedAt");
        AssertProperties<ChatMessageSenderResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified");
        AssertProperties<ChatReplyMessageResponse>("Id", "Content", "MessageType", "SenderName");
        AssertProperties<ChatMessageAttachmentResponse>("Id", "FileUrl", "FileName", "MimeType", "ThumbnailUrl", "FileSize", "Duration");
        AssertProperties<ChatMessageReactionResponse>("UserId", "DisplayName", "ReactionType");
        AssertProperties<ChatReactionSummaryResponse>("Like", "Love", "Haha", "Wow", "Sad", "Angry", "Total");
        AssertProperties<ChatParticipantResponse>("Id", "DisplayName", "AvatarUrl");
        AssertProperties<ChatConversationParticipantResponse>(
            "Id",
            "DisplayName",
            "AvatarUrl",
            "IsVerified",
            "IsStranger",
            "Friendship");
        AssertProperties<ChatFriendshipResponse>("Status", "IsRequester");
    }

    [Fact]
    public void Send_chat_message_response_contract_has_expected_fields()
    {
        AssertProperties<SendChatMessageResponse>(
            "Id",
            "ConversationId",
            "Sender",
            "MessageType",
            "Content",
            "ReplyMessage",
            "Attachments",
            "IsMine",
            "IsEdited",
            "IsDeleted",
            "CreatedAt");
    }

    [Fact]
    public void Mark_conversation_read_response_contract_has_expected_fields()
    {
        AssertProperties<MarkConversationReadResponse>(
            "ConversationId",
            "LastReadMessageId",
            "ReadAt");
        AssertProperties<MessagesReadRealtimePayload>(
            "ConversationId",
            "UserId",
            "LastReadMessageId",
            "ReadAt",
            "UnreadCount");
        AssertProperties<RecallChatMessageResponse>(
            "ConversationId",
            "MessageId",
            "DeletedBy",
            "DeletedAt");
        AssertProperties<MessageDeletedPayload>(
            "ConversationId",
            "MessageId",
            "DeletedBy",
            "DeletedAt");
    }

    [Fact]
    public void Chat_realtime_payload_contracts_have_expected_fields()
    {
        AssertProperties<ChatRealtimeMessageResponse>(
            "Id",
            "ConversationId",
            "Sender",
            "MessageType",
            "Content",
            "Reply",
            "Attachments",
            "Reactions",
            "IsMine",
            "IsEdited",
            "IsDeleted",
            "CreatedAt");
        AssertProperties<NewMessageNotificationPayload>(
            "ConversationId",
            "ConversationType",
            "ConversationName",
            "ConversationAvatarUrl",
            "Sender",
            "Message",
            "UnreadCount",
            "IsMuted");
        AssertProperties<NewMessageNotificationMessagePayload>(
            "Id",
            "Content",
            "MessageType",
            "Attachments",
            "CreatedAt");
        AssertProperties<MessageDeliveredPayload>(
            "ConversationId",
            "MessageId",
            "UserId",
            "DeliveredAt");
    }

    [Fact]
    public void Chat_messages_documents_expected_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.Messages))!;

        Assert.Equal(
            "conversations/{conversationId:guid}/messages",
            action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(ChatMessageListResponse));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status403Forbidden &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Send_chat_message_documents_expected_route_and_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.SendMessage))!;

        Assert.Equal("messages", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(SendChatMessageResponse));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status400BadRequest &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status403Forbidden &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Chat_attachment_upload_documents_expected_route_and_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.UploadAttachments))!;

        Assert.Equal("attachments/upload", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.NotNull(action.GetCustomAttribute<ConsumesAttribute>());
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(IReadOnlyList<ChatAttachmentUploadResponse>));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status400BadRequest &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Recall_chat_message_documents_expected_route_and_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.RecallMessage))!;

        Assert.Equal("messages/{messageId:guid}/recall", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(RecallChatMessageResponse));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status403Forbidden &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Mark_conversation_read_documents_expected_route_and_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.MarkRead))!;

        Assert.Equal("conversations/{conversationId:guid}/read", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(MarkConversationReadResponse));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status403Forbidden &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Set_conversation_pin_documents_expected_route_and_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.SetPin))!;

        Assert.Equal("conversations/{conversationId:guid}/pin", action.GetCustomAttribute<HttpPatchAttribute>()!.Template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(SetConversationPinResponse));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status403Forbidden &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Theory]
    [InlineData(nameof(ChatController.SetMute), "conversations/{conversationId:guid}/mute", typeof(HttpPatchAttribute), typeof(SetConversationMuteResponse))]
    [InlineData(nameof(ChatController.SetBlock), "conversations/{conversationId:guid}/block", typeof(HttpPatchAttribute), typeof(SetConversationBlockResponse))]
    [InlineData(nameof(ChatController.Info), "conversations/{conversationId:guid}", typeof(HttpGetAttribute), typeof(ChatConversationInfoResponse))]
    [InlineData(nameof(ChatController.Attachments), "conversations/{conversationId:guid}/attachments", typeof(HttpGetAttribute), typeof(ChatAttachmentListResponse))]
    [InlineData(nameof(ChatController.Links), "conversations/{conversationId:guid}/links", typeof(HttpGetAttribute), typeof(ChatLinkListResponse))]
    [InlineData(nameof(ChatController.Search), "conversations/{conversationId:guid}/search", typeof(HttpGetAttribute), typeof(ChatMessageSearchResponse))]
    public void Chat_settings_documents_expected_route_and_status_codes(
        string actionName,
        string route,
        Type httpAttributeType,
        Type responseType)
    {
        var action = typeof(ChatController).GetMethod(actionName)!;
        var httpAttribute = action.GetCustomAttributes()
            .Single(attribute => attribute.GetType() == httpAttributeType);
        var template = (string?)httpAttributeType
            .GetProperty(nameof(HttpMethodAttribute.Template))!
            .GetValue(httpAttribute);

        Assert.Equal(route, template);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == responseType);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status403Forbidden &&
                         attribute.Type == typeof(ProblemDetails));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound &&
                         attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Chat_conversations_documents_success_and_unauthorized_status_codes()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.Conversations))!;

        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                         attribute.Type == typeof(ChatConversationListResponse));
        Assert.Contains(
            action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void Chat_contract_uses_domain_enums()
    {
        Assert.Equal(typeof(ConversationType), typeof(ChatConversationItemResponse)
            .GetProperty(nameof(ChatConversationItemResponse.ConversationType))!.PropertyType);
        Assert.Equal(typeof(MessageType), typeof(ChatLastMessageResponse)
            .GetProperty(nameof(ChatLastMessageResponse.MessageType))!.PropertyType);
        Assert.Equal(typeof(MessageType), typeof(ChatMessageItemResponse)
            .GetProperty(nameof(ChatMessageItemResponse.MessageType))!.PropertyType);
        Assert.Equal(typeof(ReactionType), typeof(ChatMessageReactionResponse)
            .GetProperty(nameof(ChatMessageReactionResponse.ReactionType))!.PropertyType);
        Assert.Equal(typeof(MessageType), typeof(SendChatMessageRequest)
            .GetProperty(nameof(SendChatMessageRequest.MessageType))!.PropertyType);
        Assert.Equal(typeof(MessageType), typeof(SendChatMessageResponse)
            .GetProperty(nameof(SendChatMessageResponse.MessageType))!.PropertyType);
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
