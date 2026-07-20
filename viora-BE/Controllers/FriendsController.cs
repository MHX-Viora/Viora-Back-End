using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Social;
using Viora.Application.Chat;
using Viora.Domain.Entities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/friends")]
[Authorize]
public sealed class FriendsController(IMediator mediator, IGroupChatService groupChatService) : ControllerBase
{
    [HttpGet("selectable")]
    [ProducesResponseType<SelectableFriendListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Selectable([FromQuery] string? keyword = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        return Ok(await groupChatService.GetSelectableFriendsAsync(currentUserId, keyword, page, pageSize, cancellationToken));
    }
    [HttpGet]
    [ProducesResponseType<FriendshipListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] FriendshipStatus status = FriendshipStatus.Accepted,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(
            new GetFriendshipsQuery(currentUserId, page, pageSize, status, keyword),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("request")]
    [ProducesResponseType<SendFriendRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendRequest(
        SendFriendRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new SendFriendRequestCommand(currentUserId, request.UserId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("requests")]
    [ProducesResponseType<FriendRequestListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRequests(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new GetFriendRequestsQuery(currentUserId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{friendshipId:guid}/accept")]
    [ProducesResponseType<FriendshipActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(Guid friendshipId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new AcceptFriendRequestCommand(currentUserId, friendshipId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{friendshipId:guid}/reject")]
    [ProducesResponseType<FriendshipActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid friendshipId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new RejectFriendRequestCommand(currentUserId, friendshipId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<DeleteFriendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new DeleteFriendCommand(currentUserId, id), cancellationToken);
        return ToActionResult(result);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private IActionResult ToActionResult<T>(SocialResult<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var status = result.Error switch
        {
            SocialError.NotFound => StatusCodes.Status404NotFound,
            SocialError.Forbidden => StatusCodes.Status403Forbidden,
            SocialError.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Friend request failed",
            Detail = result.Message
        };
        problem.Extensions["code"] = result.Error?.ToString();
        return new ObjectResult(problem) { StatusCode = status };
    }
}

public sealed record SendFriendRequest([param: Required] Guid UserId);
