using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Users;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/user-settings")]
[Authorize]
public sealed class UserSettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        return ToActionResult(await mediator.Send(new GetUserSettingsQuery(userId), cancellationToken));
    }

    [HttpPatch]
    [ProducesResponseType<UserSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(UpdateUserSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var result = await mediator.Send(
            new UpdateUserSettingsCommand(
                userId,
                request.IsPrivate,
                request.AllowMessageEveryone,
                request.AllowComment,
                request.AllowMention,
                request.Language,
                request.Theme),
            cancellationToken);
        return ToActionResult(result);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private IActionResult ToActionResult<T>(UserSettingsResult<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var status = result.Error == UserSettingsError.NotFound
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails { Status = status, Title = "User settings request failed", Detail = result.Message };
        problem.Extensions["code"] = result.Error?.ToString();
        return new ObjectResult(problem) { StatusCode = status };
    }
}

public sealed record UpdateUserSettingsRequest(
    bool? IsPrivate,
    bool? AllowMessageEveryone,
    bool? AllowComment,
    bool? AllowMention,
    [param: MaxLength(20)] string? Language,
    [param: MaxLength(20)] string? Theme);
