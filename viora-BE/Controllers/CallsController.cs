using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Calls;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/calls")]
public sealed class CallsController(IMediator mediator, IIceServerProvider iceServerProvider) : ControllerBase
{
    /// <summary>Creates a 1-1 voice call session for a private conversation.</summary>
    [HttpPost]
    [ProducesResponseType<CreateCallResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Create([FromBody] CreateCallRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(new CreateCallCommand(userId, request.ConversationId), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : ToActionResult(result);
    }

    /// <summary>Accepts an incoming call.</summary>
    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken cancellationToken) =>
        await WithUser(userId => mediator.Send(new AcceptCallCommand(userId, id), cancellationToken));

    /// <summary>Rejects an incoming call.</summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken) =>
        await WithUser(userId => mediator.Send(new RejectCallCommand(userId, id), cancellationToken));

    /// <summary>Cancels a calling session before it is answered.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        await WithUser(userId => mediator.Send(new CancelCallCommand(userId, id), cancellationToken));

    /// <summary>Ends an accepted call and stores the duration.</summary>
    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken) =>
        await WithUser(userId => mediator.Send(new EndCallCommand(userId, id), cancellationToken));

    /// <summary>Returns paginated call history for the authenticated user.</summary>
    [HttpGet("history")]
    [ProducesResponseType<CallHistoryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CallHistoryResponse>> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        return Ok(await mediator.Send(new GetCallHistoryQuery(userId, page, pageSize), cancellationToken));
    }

    /// <summary>Returns a single call session visible to the authenticated user.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await WithUser(userId => mediator.Send(new GetCallByIdQuery(userId, id), cancellationToken));

    /// <summary>Returns ICE server configuration for WebRTC clients.</summary>
    [HttpGet("ice")]
    [ProducesResponseType<IceServersResponse>(StatusCodes.Status200OK)]
    public ActionResult<IceServersResponse> Ice() => Ok(iceServerProvider.Get());

    private async Task<IActionResult> WithUser<T>(Func<Guid, Task<CallResult<T>>> action)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        return ToActionResult(await action(userId));
    }

    private IActionResult ToActionResult<T>(CallResult<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var status = result.Error switch
        {
            CallError.NotFound or CallError.ConversationNotFound => StatusCodes.Status404NotFound,
            CallError.ConversationDissolved => StatusCodes.Status410Gone,
            CallError.Forbidden or CallError.Blocked => StatusCodes.Status403Forbidden,
            CallError.InvalidState => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Call request failed",
            Detail = result.Message
        };
        problem.Extensions["code"] = result.Error?.ToString();
        if (result.Error == CallError.ConversationDissolved) problem.Extensions["message"] = result.Message;
        return new ObjectResult(problem) { StatusCode = status };
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }
}
