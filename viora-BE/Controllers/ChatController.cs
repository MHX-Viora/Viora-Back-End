using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Chat;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController(IMediator mediator) : ControllerBase
{
    [HttpGet("conversations")]
    [ProducesResponseType<ChatConversationListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ChatConversationListResponse>> Conversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var response = await mediator.Send(
            new GetChatConversationsQuery(userId, page, pageSize, keyword),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    [ProducesResponseType<ChatMessageListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatMessageListResponse>> Messages(
        Guid conversationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var result = await mediator.Send(
            new GetChatConversationMessagesQuery(userId, conversationId, page, pageSize),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error == ChatError.ConversationNotFound
            ? NotFoundProblem(ChatError.ConversationNotFound, result.Message ?? "Khong tim thay cuoc tro chuyen.")
            : ForbiddenProblem(result.Message ?? "Ban khong co quyen xem cuoc tro chuyen nay.");
    }

    [HttpPost("messages")]
    [ProducesResponseType<SendChatMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SendChatMessageResponse>> SendMessage(
        [FromBody] SendChatMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var result = await mediator.Send(
            new SendChatMessageCommand(
                userId,
                request.ConversationId,
                request.ReplyMessageId,
                request.MessageType,
                request.Content,
                request.Attachments),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error switch
        {
            ChatError.ConversationNotFound =>
                NotFoundProblem(ChatError.ConversationNotFound, result.Message ?? "Khong tim thay cuoc tro chuyen."),
            ChatError.MessageNotFound =>
                NotFoundProblem(ChatError.MessageNotFound, result.Message ?? "Khong tim thay tin nhan."),
            ChatError.Forbidden =>
                ForbiddenProblem(result.Message ?? "Ban khong co quyen gui tin nhan."),
            _ => BadRequestProblem(result.Message ?? "Tin nhan khong hop le.")
        };
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private ObjectResult NotFoundProblem(ChatError code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = code == ChatError.MessageNotFound ? "Message not found" : "Conversation not found",
            Detail = detail
        };
        problem.Extensions["code"] = code.ToString();
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status404NotFound };
    }

    private ObjectResult ForbiddenProblem(string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Conversation forbidden",
            Detail = detail
        };
        problem.Extensions["code"] = ChatError.Forbidden.ToString();
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }

    private ObjectResult BadRequestProblem(string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid chat message",
            Detail = detail
        };
        problem.Extensions["code"] = ChatError.Validation.ToString();
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }
}

public sealed record SendChatMessageRequest(
    Guid ConversationId,
    Guid? ReplyMessageId,
    MessageType MessageType,
    string? Content,
    IReadOnlyList<SendChatMessageAttachmentRequest>? Attachments);
