using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Stickers;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Route("api/admin/sticker-packs")]
[Authorize(Roles = "2")]
public sealed class AdminStickerPacksController(IAdminStickerService stickers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminStickerPackResponse>>> GetPacks(CancellationToken token) =>
        Ok(await stickers.GetPacksAsync(token));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StickerPackDetailResponse>> GetPack(Guid id, CancellationToken token)
    {
        var pack = await stickers.GetPackAsync(id, token);
        return pack is null ? NotFound() : Ok(pack);
    }

    [HttpPost]
    public async Task<ActionResult<AdminStickerPackResponse>> CreatePack(SaveStickerPackRequest request, CancellationToken token)
    {
        try { var pack = await stickers.CreatePackAsync(request, token); return CreatedAtAction(nameof(GetPack), new { id = pack.Id }, pack); }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminStickerPackResponse>> UpdatePack(Guid id, SaveStickerPackRequest request, CancellationToken token)
    {
        try { var pack = await stickers.UpdatePackAsync(id, request, token); return pack is null ? NotFound() : Ok(pack); }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetPackActive(Guid id, ActiveRequest request, CancellationToken token) =>
        await stickers.SetPackActiveAsync(id, request.IsActive, token) ? NoContent() : NotFound();

    [HttpPost("{packId:guid}/stickers")]
    public async Task<ActionResult<StickerResponse>> CreateSticker(Guid packId, SaveStickerRequest request, CancellationToken token)
    {
        try { var sticker = await stickers.CreateStickerAsync(packId, request, token); return sticker is null ? NotFound() : Ok(sticker); }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
    }

    [HttpPut("stickers/{id:guid}")]
    public async Task<ActionResult<StickerResponse>> UpdateSticker(Guid id, SaveStickerRequest request, CancellationToken token)
    {
        try { var sticker = await stickers.UpdateStickerAsync(id, request, token); return sticker is null ? NotFound() : Ok(sticker); }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
    }

    [HttpPatch("stickers/{id:guid}/active")]
    public async Task<IActionResult> SetStickerActive(Guid id, ActiveRequest request, CancellationToken token) =>
        await stickers.SetStickerActiveAsync(id, request.IsActive, token) ? NoContent() : NotFound();

    [HttpPost("{packId:guid}/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadResponse>> Upload(Guid packId, [FromForm] StickerUploadRequest request, CancellationToken token)
    {
        if (request.File is null) return ValidationProblem("File la bat buoc.");
        try
        {
            await using var stream = request.File.OpenReadStream();
            var url = await stickers.UploadAsync(packId, new(stream, request.File.FileName, request.File.ContentType, request.File.Length), token);
            return Ok(new UploadResponse(url));
        }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
    }
}

public sealed record ActiveRequest(bool IsActive);
public sealed record UploadResponse(string Url);
public sealed class StickerUploadRequest { [FromForm(Name = "file")] public IFormFile? File { get; init; } }
