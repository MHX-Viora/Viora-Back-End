using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Media;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppLogoStorage(IOptions<CloudinaryOptions> options)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private Cloudinary Client()
    {
        var o = options.Value;
        if (string.IsNullOrEmpty(o.CloudName) || string.IsNullOrEmpty(o.ApiKey) || string.IsNullOrEmpty(o.ApiSecret))
            throw new DeveloperAuthException(503, "STORAGE_UNAVAILABLE", "Dịch vụ lưu trữ logo chưa sẵn sàng.");
        var client = new Cloudinary(new Account(o.CloudName, o.ApiKey, o.ApiSecret)); client.Api.Secure = true; return client;
    }
    public static void Validate(byte[] bytes, string mime)
    {
        if (bytes.Length is < 16 or > MaxBytes) throw MiniAppInput.Invalid("Logo phải nhỏ hơn 2 MB.");
        var png = bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpg = bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        var webp = System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP";
        if (!(mime == "image/png" && png || mime == "image/jpeg" && jpg || mime == "image/webp" && webp))
            throw MiniAppInput.Invalid("Chỉ nhận ảnh PNG, JPG hoặc WebP có nội dung hợp lệ.");
    }
    public async Task<string> Upload(byte[] bytes, string publicId, CancellationToken ct)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var result = await Client().UploadAsync(new ImageUploadParams
            {
                File = new FileDescription("logo", stream), PublicId = publicId, Overwrite = false,
                Transformation = new Transformation().Width(512).Height(512).Crop("fill").Gravity("center").FetchFormat("png"),
            }, ct);
            // Cloudinary decodes and re-encodes the image; signature-only fake images never become accepted logos.
            if (result.Error is not null || result.SecureUrl is null || result.Width != 512 || result.Height != 512 || result.Format != "png")
                throw MiniAppInput.Invalid("Không đọc được nội dung ảnh. Hãy chọn ảnh PNG, JPG hoặc WebP khác.");
            return result.SecureUrl.AbsoluteUri;
        }
        catch (DeveloperAuthException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { throw new DeveloperAuthException(503, "STORAGE_UNAVAILABLE", "Không thể tải logo. Vui lòng thử lại."); }
    }
    public async Task Delete(string publicId, CancellationToken ct)
    {
        var result = await Client().DestroyAsync(new DeletionParams(publicId) { Invalidate = true }).WaitAsync(ct);
        if (result.Error is not null || result.Result is not ("ok" or "not found")) throw new InvalidOperationException("Logo cleanup failed.");
    }
}

public sealed class MiniAppLogoCleanup(IServiceScopeFactory scopes, ILogger<MiniAppLogoCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<MiniAppLogoStorage>();
                var stale = await db.MiniAppLogoUploads.Where(x => x.MiniAppId == null && x.ExpiresAt < DateTime.UtcNow)
                    .OrderBy(x => x.ExpiresAt).Take(100).ToListAsync(stoppingToken);
                foreach (var upload in stale)
                {
                    try
                    {
                        // An expired ticket cannot be claimed by the API.
                        await storage.Delete(upload.PublicId, stoppingToken);
                        db.MiniAppLogoUploads.Remove(upload); await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch { logger.LogWarning("Mini App logo cleanup will retry an expired upload."); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Mini App logo cleanup is temporarily unavailable."); }
        }
    }
}
