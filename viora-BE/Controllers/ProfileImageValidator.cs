using Microsoft.AspNetCore.Http;

namespace viora_BE.Controllers;

public sealed record ProfileImageValidationResult(bool IsValid, string? Error);

public static class ProfileImageValidator
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const int HeaderLength = 12;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async Task<ProfileImageValidationResult> ValidateAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Invalid("Vui lòng chọn ảnh để tải lên.");
        }
        if (file.Length > MaxFileBytes)
        {
            return Invalid("Ảnh không được vượt quá 5 MB.");
        }

        await using var stream = file.OpenReadStream();
        var header = new byte[HeaderLength];
        var bytesRead = 0;
        while (bytesRead < header.Length)
        {
            var read = await stream.ReadAsync(
                header.AsMemory(bytesRead, header.Length - bytesRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }
            bytesRead += read;
        }

        var valid = file.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => bytesRead >= 8 && header.AsSpan(0, 8).SequenceEqual(PngSignature),
            "image/webp" => bytesRead >= 12 &&
                            header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                            header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

        return valid
            ? new ProfileImageValidationResult(true, null)
            : Invalid("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP hợp lệ.");
    }

    private static ProfileImageValidationResult Invalid(string error) => new(false, error);
}
