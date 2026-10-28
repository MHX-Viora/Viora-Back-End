using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Utilities;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/featured-brands")]
public sealed class FeaturedBrandsController(IFeaturedBrandRepository repository) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> List(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok((await repository.ListAsync(true, token)).Select(x => new { x.Id, x.Name, x.LogoUrl, x.SortOrder }));
    }
}

[ApiController]
[Route("api/admin/featured-brands")]
[Authorize(Roles = "2")]
public sealed class AdminFeaturedBrandsController(IFeaturedBrandRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await repository.ListAsync(false, token));
    }
    [HttpPost]
    public Task<IActionResult> Create(SaveFeaturedBrand input, CancellationToken token) => Save(null, input, token);
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, SaveFeaturedBrand input, CancellationToken token) => Save(id, input, token);
    private async Task<IActionResult> Save(Guid? id, SaveFeaturedBrand input, CancellationToken token)
    {
        try { return Ok(await repository.SaveAsync(id, input, token)); }
        catch (ArgumentException e) { return BadRequest(new { error = new { code = "INVALID_BRAND", message = e.Message } }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (FeaturedBrandConflictException) { return ConflictError(); }
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] long version, CancellationToken token)
    {
        if (version < 1) return BadRequest();
        try { return await repository.DeleteAsync(id, version, token) ? NoContent() : NotFound(); }
        catch (FeaturedBrandConflictException) { return ConflictError(); }
    }
    private IActionResult ConflictError() => Conflict(new { error = new { code = "BRAND_CONFLICT", message = "Thương hiệu đã thay đổi. Vui lòng tải lại trước khi lưu." } });
}
