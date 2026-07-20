using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Chat;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController(IMediator mediator, IGroupChatService groupChatService) : ControllerBase
{
    [HttpPost("conversations/private")]
    [ProducesResponseType<CreatePrivateConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<CreatePrivateConversationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreatePrivateConversationResponse>> CreatePrivateConversation(
        [FromBody] CreatePrivateConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var currentUserId)) return Unauthorized();

        var result = await mediator.Send(
            new CreatePrivateConversationCommand(currentUserId, request.UserId),
            cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            return result.Value.IsCreated
                ? StatusCode(StatusCodes.Status201Created, result.Value)
                : Ok(result.Value);
        }

        return result.Error switch
        {
            ChatError.UserNotFound => NotFoundProblem(ChatError.UserNotFound, result.Message ?? "Khong tim thay nguoi dung."),
            ChatError.Forbidden => ForbiddenProblem(result.Message ?? "Khong the tao phong chat rieng."),
            _ => BadRequestProblem(result.Message ?? "Yeu cau khong hop le.")
        };
    }

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

    [HttpPost("attachments/upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<IReadOnlyList<ChatAttachmentUploadResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ChatAttachmentUploadResponse>>> UploadAttachments(
        [FromForm] ChatAttachmentUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var files = request.Files?
            .Select(file => new CreatePostFile(
                file.OpenReadStream(),
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length))
            .ToList() ?? [];

        try
        {
            var result = await mediator.Send(
                new UploadChatAttachmentsCommand(userId, files),
                cancellationToken);

            return result.IsSuccess
                ? Ok(result.Value)
                : BadRequestProblem(result.Message ?? "Tep dinh kem khong hop le.");
        }
        catch (CreatePostException exception)
        {
            return BadRequestProblem(exception.Message);
        }
    }

    [HttpPost("messages/{messageId:guid}/recall")]
    [ProducesResponseType<RecallChatMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecallChatMessageResponse>> RecallMessage(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var result = await mediator.Send(
            new RecallChatMessageCommand(userId, messageId),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error == ChatError.MessageNotFound
            ? NotFoundProblem(ChatError.MessageNotFound, result.Message ?? "Khong tim thay tin nhan.")
            : ForbiddenProblem(result.Message ?? "Ban khong co quyen thu hoi tin nhan nay.");
    }


    [HttpPost("conversations/{conversationId:guid}/read")]
    [ProducesResponseType<MarkConversationReadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarkConversationReadResponse>> MarkRead(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var result = await mediator.Send(
            new MarkConversationReadCommand(userId, conversationId),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error == ChatError.ConversationNotFound
            ? NotFoundProblem(ChatError.ConversationNotFound, result.Message ?? "Khong tim thay cuoc tro chuyen.")
            : ForbiddenProblem(result.Message ?? "Ban khong co quyen danh dau da doc cuoc tro chuyen nay.");
    }

    [HttpPatch("conversations/{conversationId:guid}/pin")]
    [ProducesResponseType<SetConversationPinResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SetConversationPinResponse>> SetPin(
        Guid conversationId,
        [FromBody] SetConversationPinRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();

        var result = await mediator.Send(
            new SetConversationPinCommand(userId, conversationId, request.IsPinned),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error == ChatError.ConversationNotFound
            ? NotFoundProblem(ChatError.ConversationNotFound, result.Message ?? "Khong tim thay cuoc tro chuyen.")
            : ForbiddenProblem(result.Message ?? "Ban khong co quyen ghim cuoc tro chuyen nay.");
    }

    [HttpPatch("conversations/{conversationId:guid}/mute")]
    [ProducesResponseType<SetConversationMuteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SetConversationMuteResponse>> SetMute(
        Guid conversationId,
        [FromBody] SetConversationMuteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new SetConversationMuteCommand(userId, conversationId, request.IsMuted), cancellationToken);
        return ToChatActionResult(result, "Ban khong co quyen tat thong bao cuoc tro chuyen nay.");
    }

    [HttpPatch("conversations/{conversationId:guid}/block")]
    [ProducesResponseType<SetConversationBlockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SetConversationBlockResponse>> SetBlock(
        Guid conversationId,
        [FromBody] SetConversationBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new SetConversationBlockCommand(userId, conversationId, request.IsBlocked), cancellationToken);
        return ToChatActionResult(result, "Ban khong co quyen chan cuoc tro chuyen nay.");
    }

    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType<ChatConversationInfoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatConversationInfoResponse>> Info(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new GetConversationInfoQuery(userId, conversationId), cancellationToken);
        return ToChatActionResult(result, "Ban khong co quyen xem cuoc tro chuyen nay.");
    }

    [HttpGet("conversations/{conversationId:guid}/attachments")]
    [ProducesResponseType<ChatAttachmentListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatAttachmentListResponse>> Attachments(
        Guid conversationId,
        [FromQuery] ChatAttachmentFilterType type = ChatAttachmentFilterType.All,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new GetConversationAttachmentsQuery(userId, conversationId, type, page, pageSize), cancellationToken);
        return ToChatActionResult(result, "Ban khong co quyen xem tep trong cuoc tro chuyen nay.");
    }

    [HttpGet("conversations/{conversationId:guid}/links")]
    [ProducesResponseType<ChatLinkListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatLinkListResponse>> Links(
        Guid conversationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new GetConversationLinksQuery(userId, conversationId, page, pageSize), cancellationToken);
        return ToChatActionResult(result, "Ban khong co quyen xem lien ket trong cuoc tro chuyen nay.");
    }

    [HttpGet("conversations/{conversationId:guid}/search")]
    [ProducesResponseType<ChatMessageSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatMessageSearchResponse>> Search(
        Guid conversationId,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new SearchConversationMessagesQuery(userId, conversationId, keyword, page, pageSize), cancellationToken);
        return ToChatActionResult(result, "Ban khong co quyen tim kiem trong cuoc tro chuyen nay.");
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromForm] CreateGroupForm request, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        await using var stream = request.Avatar?.OpenReadStream();
        var avatar = request.Avatar is null ? null : new CreatePostFile(stream!, request.Avatar.FileName, request.Avatar.ContentType, request.Avatar.Length);
        var result = await groupChatService.CreateAsync(new(userId, request.Name, request.MemberIds ?? [], avatar), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : ToGroupActionResult(result);
    }

    [HttpGet("groups/{conversationId:guid}")]
    public async Task<IActionResult> GetGroup(Guid conversationId, CancellationToken token) => await WithUser(id => groupChatService.GetAsync(id, conversationId, token));

    [HttpGet("groups/{conversationId:guid}/members")]
    public async Task<IActionResult> GetGroupMembers(Guid conversationId, [FromQuery] string? keyword = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken token = default) =>
        await WithUser(id => groupChatService.GetMembersAsync(id, conversationId, keyword, page, pageSize, token));

    [HttpPost("groups/{conversationId:guid}/members")]
    public async Task<IActionResult> AddGroupMembers(Guid conversationId, AddGroupMembersRequest request, CancellationToken token) => await WithUser(id => groupChatService.AddMembersAsync(id, conversationId, request.MemberIds ?? [], token));

    [HttpDelete("groups/{conversationId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveGroupMember(Guid conversationId, Guid userId, CancellationToken token) => await WithUser(id => groupChatService.RemoveMemberAsync(id, conversationId, userId, token));

    [HttpPost("groups/{conversationId:guid}/leave")]
    public async Task<IActionResult> LeaveGroup(Guid conversationId, CancellationToken token) => await WithUser(id => groupChatService.LeaveAsync(id, conversationId, token));

    [HttpPut("groups/{conversationId:guid}/name")]
    [ProducesResponseType<RenameGroupResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RenameGroup(Guid conversationId, RenameGroupRequest request, CancellationToken token) => await WithUser(id => groupChatService.RenameAsync(id, conversationId, request.Name, token));

    [HttpPut("groups/{conversationId:guid}/avatar")]
    [ProducesResponseType<ChangeGroupAvatarResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeGroupAvatar(Guid conversationId, [FromForm] GroupAvatarForm request, CancellationToken token)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        if (request.Avatar is null) return BadRequestProblem("Avatar là bắt buộc.");
        await using var stream = request.Avatar.OpenReadStream();
        return ToGroupActionResult(await groupChatService.ChangeAvatarAsync(userId, conversationId, new(stream, request.Avatar.FileName, request.Avatar.ContentType, request.Avatar.Length), token));
    }

    [HttpPut("groups/{conversationId:guid}/permission")]
    [ProducesResponseType<ChangeGroupPermissionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeGroupPermission(Guid conversationId, ChangeGroupPermissionRequest request, CancellationToken token) => await WithUser(id => groupChatService.ChangePermissionAsync(id, conversationId, request.CanSendMessage, token));

    [HttpPut("groups/{conversationId:guid}/members/{userId:guid}/admin")]
    public async Task<IActionResult> PromoteGroupAdmin(Guid conversationId, Guid userId, CancellationToken token) => await WithUser(id => groupChatService.SetAdminAsync(id, conversationId, userId, true, token));

    [HttpDelete("groups/{conversationId:guid}/members/{userId:guid}/admin")]
    public async Task<IActionResult> DemoteGroupAdmin(Guid conversationId, Guid userId, CancellationToken token) => await WithUser(id => groupChatService.SetAdminAsync(id, conversationId, userId, false, token));

    [HttpPut("groups/{conversationId:guid}/owner")]
    public async Task<IActionResult> TransferGroupOwner(Guid conversationId, TransferGroupOwnerRequest request, CancellationToken token) => await WithUser(id => groupChatService.TransferOwnerAsync(id, conversationId, request.UserId, token));

    [HttpDelete("groups/{conversationId:guid}")]
    public async Task<IActionResult> DissolveGroup(Guid conversationId, CancellationToken token) => await WithUser(id => groupChatService.DissolveAsync(id, conversationId, token));

    private async Task<IActionResult> WithUser<T>(Func<Guid, Task<GroupChatResult<T>>> action)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        return ToGroupActionResult(await action(userId));
    }

    private IActionResult ToGroupActionResult<T>(GroupChatResult<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var status = result.Error switch { GroupChatError.NotFound => 404, GroupChatError.Forbidden => 403, GroupChatError.Conflict => 409, _ => 400 };
        var problem = new ProblemDetails { Status = status, Title = "Group chat request failed", Detail = result.Message };
        problem.Extensions["code"] = result.Error?.ToString();
        return new ObjectResult(problem) { StatusCode = status };
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
            Title = code switch
            {
                ChatError.MessageNotFound => "Message not found",
                ChatError.UserNotFound => "User not found",
                _ => "Conversation not found"
            },
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

    private ActionResult<T> ToChatActionResult<T>(ChatResult<T> result, string forbiddenMessage)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return result.Error switch
        {
            ChatError.ConversationNotFound => NotFoundProblem(ChatError.ConversationNotFound, result.Message ?? "Khong tim thay cuoc tro chuyen."),
            ChatError.Forbidden => ForbiddenProblem(result.Message ?? forbiddenMessage),
            _ => BadRequestProblem(result.Message ?? "Yeu cau khong hop le.")
        };
    }
}

public sealed record SendChatMessageRequest(
    Guid ConversationId,
    Guid? ReplyMessageId,
    MessageType MessageType,
    string? Content,
    IReadOnlyList<SendChatMessageAttachmentRequest>? Attachments);

public sealed class ChatAttachmentUploadRequest
{
    [FromForm(Name = "files")]
    public List<IFormFile>? Files { get; init; }
}

public sealed record SetConversationPinRequest(bool IsPinned);
public sealed record SetConversationMuteRequest(bool IsMuted);
public sealed record SetConversationBlockRequest(bool IsBlocked);

public sealed class CreateGroupForm
{
    [FromForm(Name = "name")] public string Name { get; init; } = string.Empty;
    [FromForm(Name = "avatar")] public IFormFile? Avatar { get; init; }
    [FromForm(Name = "memberIds[]")] public List<Guid>? MemberIds { get; init; }
}

public sealed class GroupAvatarForm
{
    [FromForm(Name = "avatar")] public IFormFile? Avatar { get; init; }
}
