using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Posts;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/profile/me")]
[Authorize]
public sealed class ProfileController(IMediator mediator) : ControllerBase
{
    [HttpGet("reacted-posts")]
    [ProducesResponseType<PostFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> ReactedPosts([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Get(ProfileFeedKind.ReactedPosts, page, pageSize, cancellationToken);

    [HttpGet("reacted-reels")]
    [ProducesResponseType<PostFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> ReactedReels([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Get(ProfileFeedKind.ReactedReels, page, pageSize, cancellationToken);

    [HttpGet("saved-posts")]
    [ProducesResponseType<PostFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> SavedPosts([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Get(ProfileFeedKind.SavedPosts, page, pageSize, cancellationToken);

    [HttpGet("saved-reels")]
    [ProducesResponseType<PostFeedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> SavedReels([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Get(ProfileFeedKind.SavedReels, page, pageSize, cancellationToken);

    private async Task<IActionResult> Get(ProfileFeedKind kind, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!TryGetViewerUserId(out var userId)) return Unauthorized();
        return Ok(await mediator.Send(new GetProfileFeedQuery(userId, kind, page, pageSize), cancellationToken));
    }

    private bool TryGetViewerUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }
}
