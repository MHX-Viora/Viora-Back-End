using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.GroupCalls;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/group-calls")]
public sealed class GroupCallsController(IGroupCallService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start(StartGroupCallRequest request, CancellationToken token)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await service.StartAsync(userId, request, token);
        return result.IsSuccess ? StatusCode(201, result.Value) : Error(result);
    }

    [HttpPost("{id:guid}/join")]
    public Task<IActionResult> Join(Guid id, CancellationToken token) =>
        WithUser(userId => service.JoinAsync(userId, id, token));

    [HttpPost("{id:guid}/end")]
    public Task<IActionResult> End(Guid id, CancellationToken token) =>
        WithUser(userId => service.EndAsync(userId, id, token));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken token) =>
        WithUser(userId => service.GetAsync(userId, id, token));

    [HttpGet("conversations/{conversationId:guid}/active")]
    public Task<IActionResult> Active(Guid conversationId, CancellationToken token) =>
        WithUser(userId => service.GetActiveAsync(userId, conversationId, token));

    private async Task<IActionResult> WithUser<T>(Func<Guid, Task<GroupCallResult<T>>> action)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await action(userId);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    private IActionResult Error<T>(GroupCallResult<T> result)
    {
        var status = result.Error switch
        {
            GroupCallError.NotFound => 404,
            GroupCallError.Forbidden => 403,
            GroupCallError.TooManyParticipants or GroupCallError.InvalidState => 409,
            GroupCallError.Configuration => 503,
            _ => 400
        };
        return Problem(statusCode: status, title: "Group call request failed", detail: result.Message);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue("user_id"), out userId);
}
