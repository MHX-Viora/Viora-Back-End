using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using Viora.Application.Posts;

namespace Viora.Infrastructure.Media;

public sealed class CloudinaryMediaStorage : IMediaStorage
{
    private readonly Cloudinary cloudinary;

    public CloudinaryMediaStorage(IOptions<CloudinaryOptions> options)
    {
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
}
