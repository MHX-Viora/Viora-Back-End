using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using Viora.Application.Users;

namespace Viora.Infrastructure.Media;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}

public sealed class CloudinaryProfileImageStorage : IProfileImageStorage
{
    private readonly Cloudinary cloudinary;

    public CloudinaryProfileImageStorage(IOptions<CloudinaryOptions> options)
    {
        var value = options.Value;
        cloudinary = new Cloudinary(new Account(value.CloudName, value.ApiKey, value.ApiSecret));
        // Cloudinary recommends secure=true so generated delivery URLs use HTTPS.
        // https://cloudinary.com/documentation/dotnet_integration#set_required_configuration_parameters
        cloudinary.Api.Secure = true;
    }

    public async Task<string> UploadAsync(
        ProfileImageUpload upload,
        CancellationToken cancellationToken)
    {
        try
        {
            // Signed server-side upload; the SDK returns SecureUrl from the upload response.
            // https://cloudinary.com/documentation/dotnet_image_and_video_upload#net_image_upload
            var result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription(upload.FileName, upload.Content),
                Folder = upload.Folder,
                PublicId = upload.PublicId,
                Overwrite = true,
                Invalidate = true,
                UniqueFilename = false,
                UseFilename = false
            }, cancellationToken);

            if (result.Error is not null || result.SecureUrl is null)
            {
                throw new ProfileImageStorageException("Không thể tải ảnh lên dịch vụ lưu trữ.");
            }

            return result.SecureUrl.AbsoluteUri;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProfileImageStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProfileImageStorageException("Không thể tải ảnh lên dịch vụ lưu trữ.", exception);
        }
    }
}
