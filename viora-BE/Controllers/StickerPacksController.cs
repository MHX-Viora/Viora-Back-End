using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Stickers;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/sticker-packs")]
[Authorize]
public sealed class StickerPacksController(IStickerService stickers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StickerPackListResponse>> GetPacks(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken token = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseFilter(type, out var filter))
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid sticker pack filter" });
        }
        return Ok(await stickers.GetPacksAsync(userId, filter, page, pageSize, token));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StickerPackDetailResponse>> GetPack(Guid id, CancellationToken token)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var pack = await stickers.GetPackAsync(userId, id, token);
        return pack is null ? NotFound() : Ok(pack);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue("user_id"), out userId);

    private static bool TryParseFilter(string? value, out StickerPackFilter filter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            filter = StickerPackFilter.All;
            return true;
        }
        return Enum.TryParse(value, true, out filter) && Enum.IsDefined(filter);
    }
}
