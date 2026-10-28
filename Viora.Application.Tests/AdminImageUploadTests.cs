using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Admin;
using Viora.Application.Posts;
using viora_BE.Controllers.Admin;
using Xunit;

namespace Viora.Application.Tests;

public sealed class AdminImageUploadTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static AdminImagesController Controller(Storage storage, bool identity = true)
    {
        var controller = new AdminImagesController(storage);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (identity) controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", AdminId.ToString())], "test"));
        return controller;
    }
    private static AdminImageUploadRequest Request(byte[] bytes, string type = "image/png", long? length = null) => new() {
        File = new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "file", "chosen.png") { Headers = new HeaderDictionary(), ContentType = type }
    };
    [Fact] public void Endpoint_requires_admin_and_limits_request_size()
    {
        var attribute = Assert.Single(typeof(AdminImagesController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("2", attribute.Roles);
        Assert.NotNull(typeof(AdminImagesController).GetCustomAttributes(typeof(RequestSizeLimitAttribute), true).SingleOrDefault());
    }
    [Fact] public async Task Valid_image_returns_uploaded_url_with_admin_owner()
    {
        var storage = new Storage();
        var result = await Controller(storage).Upload(Request(Png), default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("https://cdn.example/image.png", Assert.IsType<UploadResponse>(ok.Value).Url);
        Assert.Equal(AdminId, storage.Owner); Assert.Equal(Png, storage.Bytes);
    }
    [Theory]
    [InlineData("image/svg+xml", 12)] [InlineData("image/png", 0)] [InlineData("image/png", 5242881)]
    public async Task Invalid_metadata_never_reaches_storage(string type, long length)
    {
        var storage = new Storage();
        var result = await Controller(storage).Upload(Request(Png, type, length), default);
        Assert.IsType<ObjectResult>(result.Result); Assert.Null(storage.Owner);
    }
    [Fact] public async Task Spoofed_image_and_missing_file_are_rejected()
    {
        var storage = new Storage();
        await Controller(storage).Upload(Request("not an image"u8.ToArray()), default);
        await Controller(storage).Upload(new(), default);
        Assert.Null(storage.Owner);
    }
    [Fact] public async Task Missing_identity_never_uploads()
    {
        var storage = new Storage();
        Assert.IsType<UnauthorizedResult>((await Controller(storage, false).Upload(Request(Png), default)).Result);
        Assert.Null(storage.Owner);
    }
    [Fact] public async Task Storage_failure_is_sanitized()
    {
        var result = await Controller(new Storage { Fail = true }).Upload(Request(Png), default);
        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, error.StatusCode); Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(error.Value));
    }
    private sealed class Storage : IAdminImageStorage
    {
        public Guid? Owner; public byte[]? Bytes; public bool Fail;
        public async Task<string> UploadAdminImageAsync(Guid owner, CreatePostFile file, CancellationToken token)
        {
            Owner = owner;
            if (Fail) throw new CreatePostException("MEDIA_UPLOAD_FAILED", "private storage detail");
            using var memory = new MemoryStream(); await file.Content.CopyToAsync(memory, token); Bytes = memory.ToArray();
            return "https://cdn.example/image.png";
        }
    }
}
