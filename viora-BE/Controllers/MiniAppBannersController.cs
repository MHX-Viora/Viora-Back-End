using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;
namespace viora_BE.Controllers;
[ApiController, AllowAnonymous, Route("api/mini-apps/banners")]
public sealed class MiniAppBannersController(IMiniAppEditorialService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<MiniAppBannerDto>> List(CancellationToken ct) => service.BannersAsync(false, ct);
}
