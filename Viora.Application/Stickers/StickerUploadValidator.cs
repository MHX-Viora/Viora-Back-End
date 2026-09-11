namespace Viora.Application.Stickers;

public static class StickerUploadValidator
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const int HeaderLength = 12;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static Task ValidateThumbnailAsync(StickerUploadFile file, CancellationToken token) =>
        ValidateAsync(file, allowJpeg: true, token);

    public static Task ValidateStickerAsync(StickerUploadFile file, CancellationToken token) =>
        ValidateAsync(file, allowJpeg: false, token);

    private static async Task ValidateAsync(StickerUploadFile file, bool allowJpeg, CancellationToken token)
    {
        if (file.Length <= 0)
            throw new ArgumentException("Vui long chon anh de tai len.");
        if (file.Length > MaxFileBytes)
            throw new ArgumentException("Anh khong duoc vuot qua 5 MB.");

        var originalPosition = file.Content.CanSeek ? file.Content.Position : 0;
        var header = new byte[HeaderLength];
        var bytesRead = 0;
        try
        {
            while (bytesRead < header.Length)
            {
                var read = await file.Content.ReadAsync(
                    header.AsMemory(bytesRead, header.Length - bytesRead), token);
                if (read == 0) break;
                bytesRead += read;
            }
        }
        finally
        {
            if (file.Content.CanSeek) file.Content.Position = originalPosition;
        }

        var contentType = file.ContentType.ToLowerInvariant();
        var valid = contentType switch
        {
            "image/jpeg" when allowJpeg => bytesRead >= 3 &&
                                               header[0] == 0xFF &&
                                               header[1] == 0xD8 &&
                                               header[2] == 0xFF,
            "image/png" => bytesRead >= 8 && header.AsSpan(0, 8).SequenceEqual(PngSignature),
            "image/webp" => bytesRead >= 12 &&
                            header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                            header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

        if (!valid)
        {
            var formats = allowJpeg ? "JPEG, PNG hoac WebP" : "PNG hoac WebP";
            throw new ArgumentException($"Chi chap nhan anh {formats} hop le.");
        }
    }
}
