using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Live;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using viora_BE.Controllers;
using Xunit;

public sealed class LiveGiftUploadTests
{
    private static byte[] Animation(int frames = 240, int fps = 30)
    {
        static void Varint(Stream stream, uint number) { while (number > 127) { stream.WriteByte((byte)(number | 128)); number >>= 7; } stream.WriteByte((byte)number); }
        using var parameters = new MemoryStream(); parameters.WriteByte(13); parameters.Write(BitConverter.GetBytes(750f));
        parameters.WriteByte(21); parameters.Write(BitConverter.GetBytes(1334f)); parameters.WriteByte(24); Varint(parameters, (uint)fps);
        parameters.WriteByte(32); Varint(parameters, (uint)frames);
        using var movie = new MemoryStream(); movie.WriteByte(18); Varint(movie, (uint)parameters.Length); parameters.Position = 0; parameters.CopyTo(movie); movie.Write([34, 0]);
        using var result = new MemoryStream(); using (var zlib = new ZLibStream(result, CompressionMode.Compress, true)) zlib.Write(movie.ToArray());
        return result.ToArray();
    }
    [Fact]
    public void SvgaMetadataRejectsInvalidOrUnboundedAnimations()
    {
        Assert.Equal(8000, SvgaMetadata.Duration(Animation()));
        Assert.Equal(6467, SvgaMetadata.Duration(Animation(97, 15)));
        Assert.Equal(3300, SvgaMetadata.Duration(Animation(66, 20)));
        Assert.Throws<ArgumentException>(() => SvgaMetadata.Duration([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => SvgaMetadata.Duration(Animation(1800, 1)));
        Assert.Throws<ArgumentException>(() => SvgaMetadata.Duration(new byte[SvgaMetadata.MaxFileBytes + 1]));
        Assert.Throws<ArgumentException>(() => SvgaMetadata.Duration(new byte[40]));
    }
    [Fact]
    public async Task GiftAndAnimationPersistTogetherAndCanBeReplacedOrRemoved()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var controller = new LiveGiftsController(db) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var body = new LiveGiftBody("Sư tử", "https://example.test/lion.png", null, 1000, LiveGiftAnimationType.Fullscreen, 0, false);
        LiveGiftUploadRequest Upload(byte[]? bytes = null, bool remove = false) => new() { Metadata = JsonSerializer.Serialize(body), RemoveAnimation = remove,
            Animation = bytes is null ? null : new FormFile(new MemoryStream(bytes), 0, bytes.Length, "animation", "gift.svga") };
        var created = Assert.IsType<CreatedResult>(await controller.UploadCreate(Upload(Animation()), default));
        var dto = Assert.IsType<LiveGiftDto>(created.Value); Assert.Equal(8000, dto.AnimationDurationMs); Assert.EndsWith("/animation.svga", dto.AnimationUrl);
        var saved = await db.LiveGifts.AsNoTracking().SingleAsync(); Assert.Equal(Animation(), saved.AnimationData); Assert.False(saved.IsActive);
        var file = Assert.IsType<FileContentResult>(await controller.Animation(saved.Id, default)); Assert.Equal(Animation(), file.FileContents);
        body = body with { Name = "Tên lửa", Price = 2000, IsActive = true };
        Assert.IsType<OkObjectResult>(await controller.UploadUpdate(saved.Id, Upload(), default));
        Assert.Equal(8000, (await db.LiveGifts.AsNoTracking().SingleAsync()).AnimationDurationMs);
        var active = Assert.IsType<OkObjectResult>(await controller.Active(default)); Assert.Single(Assert.IsAssignableFrom<IEnumerable<LiveGiftDto>>(active.Value));
        Assert.IsType<OkObjectResult>(await controller.UploadUpdate(saved.Id, Upload(Animation(66, 20)), default));
        Assert.Equal(3300, (await db.LiveGifts.AsNoTracking().SingleAsync()).AnimationDurationMs);
        Assert.IsType<UnprocessableEntityObjectResult>(await controller.UploadUpdate(saved.Id, Upload([1, 2, 3]), default));
        Assert.Equal(3300, (await db.LiveGifts.AsNoTracking().SingleAsync()).AnimationDurationMs);
        Assert.IsType<OkObjectResult>(await controller.UploadUpdate(saved.Id, Upload(remove: true), default));
        Assert.Null((await db.LiveGifts.AsNoTracking().SingleAsync()).AnimationData);
        Assert.IsType<NotFoundResult>(await controller.Animation(saved.Id, default));
        body = body with { Price = 0 }; Assert.IsType<UnprocessableEntityObjectResult>(await controller.UploadCreate(Upload(), default));
    }
    [Theory]
    [InlineData(nameof(LiveGiftsController.UploadCreate))]
    [InlineData(nameof(LiveGiftsController.UploadUpdate))]
    public void UploadMutationsRequireAdminRole(string method)
    {
        var attribute = Assert.IsType<AuthorizeAttribute>(typeof(LiveGiftsController).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), false).Single());
        Assert.Equal("2", attribute.Roles);
    }
}
