using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;
namespace viora_BE.Controllers.Admin;

[ApiController, Authorize(Roles = "2"), Route("api/admin/mini-app-editorial")]
public sealed class AdminMiniAppEditorialController(IMiniAppEditorialService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
    [HttpGet("banners")] public Task<IReadOnlyList<MiniAppBannerDto>> Banners(CancellationToken ct) => service.BannersAsync(true, ct);
    [HttpPost("banners")] public Task<MiniAppBannerDto> Create(MiniAppBannerInput input, CancellationToken ct) => service.SaveBannerAsync(Actor, null, input, ct);
    [HttpPut("banners/{id:guid}")] public Task<MiniAppBannerDto> Update(Guid id, MiniAppBannerInput input, CancellationToken ct) => service.SaveBannerAsync(Actor, id, input, ct);
    [HttpDelete("banners/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.DeleteBannerAsync(Actor, id, ct); return NoContent(); }
    [HttpGet("placements")] public Task<IReadOnlyList<MiniAppPlacementDto>> Placements(CancellationToken ct) => service.PlacementsAsync(ct);
    [HttpPut("placements/{id:guid}")] public async Task<IActionResult> Placement(Guid id, MiniAppPlacementInput input, CancellationToken ct) { await service.SavePlacementAsync(Actor, id, input, ct); return NoContent(); }
    [HttpPut("visibility/{id:guid}")] public async Task<IActionResult> Visibility(Guid id, MiniAppVisibilityInput input, CancellationToken ct) { await service.SetVisibilityAsync(Actor, id, input.Hidden, ct); return NoContent(); }
}
