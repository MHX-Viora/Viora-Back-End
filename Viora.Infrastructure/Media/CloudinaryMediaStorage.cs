using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Posts;

namespace Viora.Infrastructure.Media;

public sealed class CloudinaryMediaStorage : IMediaStorage
{
    private readonly Cloudinary cloudinary;
    private readonly ILogger<CloudinaryMediaStorage> logger;

    public CloudinaryMediaStorage(IOptions<CloudinaryOptions> options, ILogger<CloudinaryMediaStorage> logger)
    {
        this.logger = logger;
        var value = options.Value;
        cloudinary = new Cloudinary(new Account(value.CloudName, value.ApiKey, value.ApiSecret));
        cloudinary.Api.Secure = true;
    }

    public async Task<UploadedMedia> UploadPostImageAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription(file.FileName, file.Content),
                Folder = $"viora/users/{userId:N}/posts",
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            }, cancellationToken);

            if (result.Error is not null || result.SecureUrl is null)
            {
                throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Không thể tải ảnh lên dịch vụ lưu trữ.");
            }

            return new UploadedMedia(result.SecureUrl.AbsoluteUri, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CreatePostException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Không thể tải ảnh lên dịch vụ lưu trữ.", exception);
        }
    }

    public async Task<UploadedMedia> UploadReelVideoAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Starting Cloudinary reel upload. UserId={UserId}, FileName={FileName}, ContentType={ContentType}, Length={Length}",
                userId,
                file.FileName,
                file.ContentType,
                file.Length);

            var result = await cloudinary.UploadAsync(new VideoUploadParams
            {
                File = new FileDescription(file.FileName, file.Content),
                Folder = $"viora/users/{userId:N}/reels",
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            }, cancellationToken);

            if (result.Error is not null || result.SecureUrl is null)
            {
                logger.LogWarning(
                    "Cloudinary reel upload failed. UserId={UserId}, Error={Error}",
                    userId,
                    result.Error?.Message);

                throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Khong the tai video len dich vu luu tru.");
            }

            var thumbnailUrl = cloudinary.Api.UrlVideoUp
                .Transform(new Transformation().StartOffset("0").Crop("fill").Width(720).Height(1280).FetchFormat("jpg"))
                .BuildUrl(result.PublicId);

            logger.LogInformation(
                "Completed Cloudinary reel upload. UserId={UserId}, PublicId={PublicId}",
                userId,
                result.PublicId);

            return new UploadedMedia(result.SecureUrl.AbsoluteUri, thumbnailUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CreatePostException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected Cloudinary reel upload exception. UserId={UserId}", userId);
            throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Khong the tai video len dich vu luu tru.", exception);
        }
    }

    public async Task<UploadedChatAttachment> UploadChatAttachmentAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var folder = $"viora/users/{userId:N}/chat";
            if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var result = await cloudinary.UploadAsync(new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, file.Content),
                    Folder = folder,
                    UseFilename = false,
                    UniqueFilename = true,
                    Overwrite = false
                }, cancellationToken);

                EnsureUploadSucceeded(result.Error, result.SecureUrl);
                return new UploadedChatAttachment(
                    result.SecureUrl.AbsoluteUri,
                    file.FileName,
                    file.ContentType,
                    null,
                    file.Length,
                    null);
            }

            if (file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                var result = await cloudinary.UploadAsync(new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, file.Content),
                    Folder = folder,
                    UseFilename = false,
                    UniqueFilename = true,
                    Overwrite = false
                }, cancellationToken);

                EnsureUploadSucceeded(result.Error, result.SecureUrl);
                var thumbnailUrl = file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                    ? cloudinary.Api.UrlVideoUp
                        .Transform(new Transformation().StartOffset("0").Crop("fill").Width(720).Height(720).FetchFormat("jpg"))
                        .BuildUrl(result.PublicId)
                    : null;

                return new UploadedChatAttachment(
                    result.SecureUrl.AbsoluteUri,
                    file.FileName,
                    file.ContentType,
                    thumbnailUrl,
                    file.Length,
                    result.Duration > 0 ? (int)Math.Round(result.Duration) : null);
            }

            var rawResult = await cloudinary.UploadAsync(new RawUploadParams
            {
                File = new FileDescription(file.FileName, file.Content),
                Folder = folder,
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            }, "raw", cancellationToken);

            EnsureUploadSucceeded(rawResult.Error, rawResult.SecureUrl);
            return new UploadedChatAttachment(
                rawResult.SecureUrl.AbsoluteUri,
                file.FileName,
                file.ContentType,
                null,
                file.Length,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CreatePostException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected Cloudinary chat attachment upload exception. UserId={UserId}", userId);
            throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Khong the tai tep chat len dich vu luu tru.", exception);
        }
    }

    public async Task<UploadedMedia> UploadGroupAvatarAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription(file.FileName, file.Content),
                Folder = $"viora/users/{userId:N}/groups",
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            }, cancellationToken);

            EnsureUploadSucceeded(result.Error, result.SecureUrl);
            return new UploadedMedia(result.SecureUrl!.AbsoluteUri, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (CreatePostException) { throw; }
        catch (Exception exception)
        {
            throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Không thể tải avatar nhóm lên dịch vụ lưu trữ.", exception);
        }
    }

    private static void EnsureUploadSucceeded(Error? error, Uri? secureUrl)
    {
        if (error is not null || secureUrl is null)
        {
            throw new CreatePostException("MEDIA_UPLOAD_FAILED", "Khong the tai tep len dich vu luu tru.");
        }
    }
}
