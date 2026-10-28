using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Utilities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/utility-banners")]
public sealed class UtilityBannersController(IUtilityBannerRepository repository) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> List(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        var items = await repository.ListAsync(true, token);
        return Ok(items.Select(x => new { x.Id, x.Title, x.ImageUrl, x.TargetUrl, x.SortOrder }));
    }
}

[ApiController]
[Route("api/admin/utility-banners")]
[Authorize(Roles = "2")]
public sealed class AdminUtilityBannersController(IUtilityBannerRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await repository.ListAsync(false, token));
    }

    [HttpPost]
    public Task<IActionResult> Create(SaveUtilityBanner input, CancellationToken token) => Save(null, input, token);

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, SaveUtilityBanner input, CancellationToken token) => Save(id, input, token);

    private async Task<IActionResult> Save(Guid? id, SaveUtilityBanner input, CancellationToken token)
    {
        try { return Ok(await repository.SaveAsync(id, input, token)); }
        catch (ArgumentException e) { return BadRequest(new { error = new { code = "INVALID_BANNER", message = e.Message } }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UtilityBannerConflictException) { return ConflictError(); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] long version, CancellationToken token)
    {
        if (version < 1) return BadRequest();
        try { return await repository.DeleteAsync(id, version, token) ? NoContent() : NotFound(); }
        catch (UtilityBannerConflictException) { return ConflictError(); }
    }

    private IActionResult ConflictError() => Conflict(new { error = new { code = "BANNER_CONFLICT", message = "Banner đã thay đổi. Vui lòng tải lại trước khi lưu." } });
}
