using Viora.Application.Stickers;
using Xunit;

namespace Viora.Application.Tests.Stickers;

public sealed class StickerUploadValidatorTests
{
    [Fact]
    public async Task Thumbnail_accepts_a_valid_jpeg_signature()
    {
        var file = File("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0]);

        await StickerUploadValidator.ValidateThumbnailAsync(file, CancellationToken.None);
    }

    [Fact]
    public async Task Thumbnail_rejects_spoofed_image_content_type()
    {
        var file = File("image/png", "not-an-image"u8.ToArray());

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            StickerUploadValidator.ValidateThumbnailAsync(file, CancellationToken.None));

        Assert.Contains("JPEG, PNG", error.Message);
    }

    private static StickerUploadFile File(string contentType, byte[] bytes) =>
        new(new MemoryStream(bytes), "thumbnail", contentType, bytes.Length);
}
