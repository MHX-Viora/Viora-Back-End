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

public sealed record CreateReelCommand(
    Guid UserId,
    string? Content,
    IReadOnlyList<string> Hashtags,
    CreatePostFile? Video) : IRequest<CreateReelResponse>;

public sealed record CreatePostFile(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

public sealed record UploadedMedia(
    string MediaUrl,
    string? ThumbnailUrl);

public sealed record UploadedChatAttachment(
    string FileUrl,
    string FileName,
    string? MimeType,
    string? ThumbnailUrl,
    long FileSize,
    int? Duration);

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

public sealed record CreateReelResponse(
    Guid Id,
    string? Content,
    PostType PostType,
    PostVisibility Visibility,
    int ReactionCount,
    int CommentCount,
    int ShareCount,
    int SaveCount,
    int ViewCount,
    DateTime CreatedAt,
    PostFeedUserResponse User,
    IReadOnlyList<PostFeedMediaResponse> Media,
    IReadOnlyList<ReelHashtagResponse> Hashtags,
    bool IsReacted,
    bool IsSaved);

public sealed record ReelHashtagResponse(
    Guid Id,
    string Name);

public interface IMediaStorage
{
    Task<UploadedMedia> UploadPostImageAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken);

    Task<UploadedMedia> UploadReelVideoAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken);

    Task<UploadedChatAttachment> UploadChatAttachmentAsync(
        Guid userId,
        CreatePostFile file,
        CancellationToken cancellationToken);
}

public interface IPostRepository
{
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Hashtag>> GetHashtagsByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken);
    Task AddAsync(Post post, CancellationToken cancellationToken);
    Task AddHashtagAsync(Hashtag hashtag, CancellationToken cancellationToken);
    Task AddPostHashtagAsync(PostHashtag postHashtag, CancellationToken cancellationToken);
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
