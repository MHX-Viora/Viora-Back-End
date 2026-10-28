using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Admin;
using Viora.Application.Posts;
using viora_BE.Controllers.Admin;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Viora.Domain.Entities;
using Viora.Infrastructure.Security;

namespace Viora.Application.Tests;

public sealed class AdminImageUploadTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static AdminImagesController Controller(Storage storage, bool identity = true)
    {
        var controller = new AdminImagesController(storage);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (identity) controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AdminId.ToString()), new Claim("user_id", Guid.NewGuid().ToString())], "test"));
        return controller;
    }
    private static AdminImageUploadRequest Request(byte[] bytes, string type = "image/png", long? length = null) => new() {
        File = new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "file", "chosen.png") { Headers = new HeaderDictionary(), ContentType = type }
    };
    [Fact] public async Task Real_jwt_admin_without_profile_can_upload_but_other_roles_and_guests_cannot()
    {
        const string key = "admin-image-upload-test-key-at-least-32-bytes";
        var options = new JwtOptions { Key = key, Issuer = "upload-test", Audience = "admin-test" };
        var tokens = new JwtTokenService(Options.Create(options));
        var storage = new Storage();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IAdminImageStorage>(storage);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt => {
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidIssuer = options.Issuer, ValidAudience = options.Audience, ValidateLifetime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], NameClaimType = "sub", RoleClaimType = "role"
            };
        });
        builder.Services.AddAuthorization(); builder.Services.AddControllers().AddApplicationPart(typeof(AdminImagesController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        async Task<HttpResponseMessage> Upload(string? token)
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
            using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent(Png); content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, "file", "image.png");
            return await client.PostAsync("/api/admin/images", form);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(null)).StatusCode);
        var nonAdmin = new Account { Id = Guid.NewGuid(), Role = AccountRole.User };
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(tokens.CreateTokens(nonAdmin).Tokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload("invalid.jwt.signature")).StatusCode);
        Assert.Null(storage.Owner);
        var admin = new Account { Id = AdminId, Role = AccountRole.Admin };
        Assert.Null(admin.User);
        Assert.Equal(HttpStatusCode.OK, (await Upload(tokens.CreateTokens(admin).Tokens.AccessToken)).StatusCode);
        Assert.Equal(AdminId, storage.Owner);
    }
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
