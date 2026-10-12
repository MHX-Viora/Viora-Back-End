using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers;

[ApiController, Authorize, Route("api/mini-apps")]
public sealed class MiniAppCatalogController(IMiniAppCatalogService service) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new MiniAppException("UNAUTHORIZED", "Authentication required.", 401);

    [HttpGet("catalog")]
    public Task<MiniAppCatalogPage> Catalog([FromQuery] MiniAppCatalogQuery query, CancellationToken ct) => service.GetCatalogAsync(AccountId, query, ct);

    [HttpPut("{id:guid}/favorite")]
    public async Task<IActionResult> Favorite(Guid id, MiniAppFavoriteInput input, CancellationToken ct)
    { await service.SetFavoriteAsync(AccountId, id, input.Favorite, ct); return NoContent(); }

    [HttpPut("favorites/order")]
    public async Task<IActionResult> Order(MiniAppFavoriteOrder input, CancellationToken ct)
    { await service.ReorderFavoritesAsync(AccountId, input, ct); return NoContent(); }

    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory(CancellationToken ct)
    { await service.ClearHistoryAsync(AccountId, ct); return NoContent(); }
}
