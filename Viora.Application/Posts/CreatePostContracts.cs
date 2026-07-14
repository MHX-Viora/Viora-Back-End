using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed record CreatePostCommand(
    Guid UserId,
    string? Content,
    PostVisibility Visibility,
    double? Latitude,
    double? Longitude,
    string? LocationName,
    string? Link,
    IReadOnlyList<CreatePostFile> Files) : IRequest<CreatePostResponse>;

public sealed record CreatePostFile(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

public sealed record UploadedMedia(
    string MediaUrl,
    string? ThumbnailUrl);

public sealed record CreatePostResponse(
    Guid Id,
    string? Content,
    PostVisibility Visibility,
    string? LocationName,
    double? Latitude,
    double? Longitude,
    string? Link,
    IReadOnlyList<CreatePostMediaResponse> Media,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    DateTime CreatedAt);

public sealed record CreatePostMediaResponse(
    Guid Id,
    string Url,
    string? ThumbnailUrl);

public interface IMediaStorage
{
    Task<UploadedMedia> UploadPostImageAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken);
}

public interface IPostRepository
{
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    Task AddAsync(Post post, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public sealed class CreatePostException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
