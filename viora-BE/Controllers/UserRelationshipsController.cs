using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Social;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UserRelationshipsController(IMediator mediator) : ControllerBase
{
    [HttpGet("me/statistics")]
    [ProducesResponseType<UserStatisticsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyStatistics(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new GetMyStatisticsQuery(currentUserId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{userId:guid}/follow")]
    [ProducesResponseType<FollowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleFollow(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new ToggleFollowCommand(currentUserId, userId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{userId:guid}/profile")]
    [ProducesResponseType<UserProfileSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserProfile(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new GetUserProfileQuery(currentUserId, userId), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{userId:guid}/relationship")]
    [ProducesResponseType<RelationshipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRelationship(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        var result = await mediator.Send(new GetRelationshipQuery(currentUserId, userId), cancellationToken);
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
            Title = "Social request failed",
            Detail = result.Message
        };
        problem.Extensions["code"] = result.Error?.ToString();
        return new ObjectResult(problem) { StatusCode = status };
    }
}
