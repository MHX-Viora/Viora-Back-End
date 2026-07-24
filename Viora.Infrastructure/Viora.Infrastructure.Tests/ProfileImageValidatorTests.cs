using Microsoft.AspNetCore.Http;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class ProfileImageValidatorTests
{
    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 })]
    public async Task ValidateAsync_accepts_supported_image_signatures(string contentType, byte[] bytes)
    {
        var file = FormFile(bytes, contentType);

        var result = await ProfileImageValidator.ValidateAsync(file, CancellationToken.None);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public async Task ValidateAsync_rejects_spoofed_content_type()
    {
        var file = FormFile([0x25, 0x50, 0x44, 0x46], "image/jpeg");

        var result = await ProfileImageValidator.ValidateAsync(file, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_rejects_files_over_five_megabytes()
    {
        var file = new FormFile(Stream.Null, 0, ProfileImageValidator.MaxFileBytes + 1, "file", "large.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var result = await ProfileImageValidator.ValidateAsync(file, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    private static FormFile FormFile(byte[] bytes, string contentType) => new(
        new MemoryStream(bytes), 0, bytes.Length, "file", "image.bin")
    {
        Headers = new HeaderDictionary(),
        ContentType = contentType
    };
}
