using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Admin;
using Viora.Application.Posts;
using Viora.Application.Stickers;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Route("api/admin/images")]
[Authorize(Roles = "2")]
[RequestSizeLimit(5 * 1024 * 1024 + 65536)]
public sealed class AdminImagesController(IAdminImageStorage storage) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadResponse>> Upload([FromForm] AdminImageUploadRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue("user_id"), out var adminId)) return Unauthorized();
        if (request.File is null) return ValidationProblem("Vui lòng chọn ảnh.");
        try
        {
            await using var stream = request.File.OpenReadStream();
            await StickerUploadValidator.ValidateThumbnailAsync(new(stream, request.File.FileName, request.File.ContentType, request.File.Length), token);
            var url = await storage.UploadAdminImageAsync(adminId, new(stream, request.File.FileName, request.File.ContentType, request.File.Length), token);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
                throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Invalid storage URL.");
            return Ok(new UploadResponse(url));
        }
        catch (ArgumentException error) { return ValidationProblem(error.Message); }
        catch (CreatePostException)
        {
            return StatusCode(503, new { error = new { code = "MEDIA_UPLOAD_FAILED", message = "Không thể tải ảnh lên. Vui lòng thử lại." } });
        }
    }
}

public sealed class AdminImageUploadRequest
{
    [FromForm(Name = "file")] public IFormFile? File { get; init; }
}
