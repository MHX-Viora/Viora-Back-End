using FluentValidation;
using MediatR;
using Viora.Domain.Entities;
using Viora.Application.Mentions;

namespace Viora.Application.Posts;

public sealed class CreatePostCommandHandler(
    IPostRepository repository,
    IUnitOfWork unitOfWork,
    IMediaStorage mediaStorage,
    IValidator<CreatePostCommand> validator,
    IMentionService mentionService)
    : IRequestHandler<CreatePostCommand, CreatePostResponse>
{
    public async Task<CreatePostResponse> Handle(
        CreatePostCommand request,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await repository.GetUserAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            throw new CreatePostException("USER_NOT_FOUND", "Không tìm thấy người dùng.");
        }

        var uploads = new List<UploadedMedia>(request.Files.Count);
        foreach (var file in request.Files)
        {
            try
            {
                uploads.Add(await mediaStorage.UploadPostImageAsync(request.UserId, file, cancellationToken));
            }
            catch (CreatePostException exception)
            {
                throw new CreatePostException(exception.Code,
                    $"Không thể tải ảnh thứ {uploads.Count + 1}/{request.Files.Count}. Bài viết chưa được tạo. Vui lòng thử lại.", exception);
            }
        }

        var post = new Post
        {
            UserId = user.Id,
            User = user,
            Content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim(),
            PostType = PostType.Post,
            Visibility = request.Visibility,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Location = string.IsNullOrWhiteSpace(request.LocationName) ? null : request.LocationName.Trim(),
            Link = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim()
        };

        var mediaCreatedAt = DateTime.UtcNow;
        foreach (var (upload, index) in uploads.Select((upload, index) => (upload, index)))
        {
            post.Media.Add(new PostMedia
            {
                Post = post,
                // PostgreSQL stores microseconds; distinct timestamps preserve selection order without a new column.
                CreatedAt = mediaCreatedAt.AddTicks(index * 10L),
                MediaUrl = upload.MediaUrl,
                ThumbnailUrl = upload.ThumbnailUrl
            });
        }

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddAsync(post, token);
        }, cancellationToken);

        var mentions = await mentionService.CreateAsync(
            user.Id,
            post.Id,
            MentionTargetType.Post,
            request.MentionUserIds,
            cancellationToken);

        return new CreatePostResponse(
            post.Id,
            post.Content,
            post.Visibility,
            post.Location,
            post.Latitude,
            post.Longitude,
            post.Link,
            post.Media.Select(media => new CreatePostMediaResponse(
                media.Id,
                media.MediaUrl,
                media.ThumbnailUrl)).ToList(),
            post.ReactionCount,
            post.CommentCount,
            post.ShareCount,
            post.SaveCount,
            post.ViewCount,
            post.CreatedAt)
        {
            Mentions = mentions
        };
    }
}
