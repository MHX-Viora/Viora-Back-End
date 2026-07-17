using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Chat;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ChatApiContractTests
{
    [Fact]
    public void Chat_controller_exposes_authenticated_conversations_route()
    {
        Assert.NotNull(typeof(ChatController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/chat", typeof(ChatController).GetCustomAttribute<RouteAttribute>()!.Template);

        var action = typeof(ChatController).GetMethod(nameof(ChatController.Conversations))!;
        Assert.Equal("conversations", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
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
    public void Mark_conversation_read_has_no_body_and_uses_route_conversation_id()
    {
        var action = typeof(ChatController).GetMethod(nameof(ChatController.MarkRead))!;
        var parameters = action.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.Equal(typeof(Guid), parameters["conversationId"].ParameterType);
        Assert.DoesNotContain(parameters.Keys, name => name.Contains("user", StringComparison.OrdinalIgnoreCase));
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
            "MemberCount",
            "LastMessage",
            "UnreadCount",
            "IsMuted",
            "IsPinned",
            "LastMessageAt");
        AssertProperties<ChatLastMessageResponse>(
            "Id",
            "SenderId",
            "SenderName",
            "MessageType",
            "Content",
            "CreatedAt",
            "IsMine");
    }

    [Fact]
    public void Chat_message_response_contract_has_expected_fields()
    {
        AssertProperties<ChatMessageListResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
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
        AssertProperties<ChatMessageAttachmentResponse>("Id", "FileUrl", "FileName", "MimeType", "FileSize", "Duration");
        AssertProperties<ChatMessageReactionResponse>("UserId", "DisplayName", "ReactionType");
        AssertProperties<ChatReactionSummaryResponse>("Like", "Love", "Haha", "Wow", "Sad", "Angry", "Total");
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
            "ReadAt");
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
