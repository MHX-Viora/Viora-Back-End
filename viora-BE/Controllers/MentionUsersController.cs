using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Mentions;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class MentionUsersController(IMentionService mentionService) : ControllerBase
{
    [HttpGet("search-mention")]
    [ProducesResponseType<IReadOnlyList<MentionSearchResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MentionSearchResponse>>> Search(
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var userId)) return Unauthorized();
        return Ok(await mentionService.SearchAsync(userId, keyword, cancellationToken));
    }
}
