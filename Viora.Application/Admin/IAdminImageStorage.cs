using Viora.Application.Posts;

namespace Viora.Application.Admin;

public interface IAdminImageStorage
{
    Task<string> UploadAdminImageAsync(Guid adminId, CreatePostFile file, CancellationToken token);
}
