using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Social;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/friends")]
[Authorize]
public sealed class FriendsController(IMediator mediator) : ControllerBase
{
    [HttpPost("request")]
    [ProducesResponseType<FriendshipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
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

    [HttpPost("{friendshipId:guid}/accept")]
    [ProducesResponseType<AcceptFriendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(Guid friendshipId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new AcceptFriendRequestCommand(currentUserId, friendshipId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{friendshipId:guid}/reject")]
    [ProducesResponseType<FriendshipResponse>(StatusCodes.Status200OK)]
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
