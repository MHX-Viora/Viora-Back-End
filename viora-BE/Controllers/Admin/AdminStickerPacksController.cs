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
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<AdminStickerPackResponse>> CreatePack([FromForm] CreateStickerPackForm request, CancellationToken token)
    {
        if (request.Thumbnail is null) return ValidationProblem("Anh dai dien la bat buoc.");
        try
        {
            await using var stream = request.Thumbnail.OpenReadStream();
            var file = new StickerUploadFile(stream, request.Thumbnail.FileName, request.Thumbnail.ContentType, request.Thumbnail.Length);
            var pack = await stickers.CreatePackAsync(new CreateStickerPackRequest(
                request.Name, request.Description, request.Price, request.IsFeatured,
                request.IsActive, request.AvailableFrom,
                request.AvailableUntil, file), token);
            return CreatedAtAction(nameof(GetPack), new { id = pack.Id }, pack);
        }
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

    [HttpPost("{packId:guid}/thumbnail")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadResponse>> UploadThumbnail(Guid packId, [FromForm] StickerUploadRequest request, CancellationToken token)
    {
        if (request.File is null) return ValidationProblem("File la bat buoc.");
        try
        {
            await using var stream = request.File.OpenReadStream();
            var url = await stickers.UploadThumbnailAsync(packId, new(stream, request.File.FileName, request.File.ContentType, request.File.Length), token);
            return Ok(new UploadResponse(url));
        }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
    }
}

public sealed record ActiveRequest(bool IsActive);
public sealed record UploadResponse(string Url);
public sealed class StickerUploadRequest { [FromForm(Name = "file")] public IFormFile? File { get; init; } }
public sealed class CreateStickerPackForm
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime? AvailableFrom { get; init; }
    public DateTime? AvailableUntil { get; init; }
    [FromForm(Name = "thumbnail")] public IFormFile? Thumbnail { get; init; }
}
