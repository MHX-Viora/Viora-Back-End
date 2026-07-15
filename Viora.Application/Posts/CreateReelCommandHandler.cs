using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed class CreateReelCommandHandler(
    IPostRepository repository,
    IUnitOfWork unitOfWork,
    IMediaStorage mediaStorage,
    IValidator<CreateReelCommand> validator)
    : IRequestHandler<CreateReelCommand, CreateReelResponse>
{
    public async Task<CreateReelResponse> Handle(CreateReelCommand request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await repository.GetUserAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            throw new CreatePostException("USER_NOT_FOUND", "Khong tim thay nguoi dung.");
        }

        var normalizedHashtags = NormalizeHashtags(request.Hashtags);
        var existingHashtags = await repository.GetHashtagsByNamesAsync(normalizedHashtags, cancellationToken);
        var hashtagByName = existingHashtags.ToDictionary(
            hashtag => hashtag.Name,
            StringComparer.OrdinalIgnoreCase);

        var upload = await mediaStorage.UploadReelVideoAsync(request.UserId, request.Video!, cancellationToken);

        var post = new Post
        {
            UserId = user.Id,
            User = user,
            Content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim(),
            PostType = PostType.ShortVideo,
            Visibility = PostVisibility.Public,
            Status = PostStatus.Published
        };

        var media = new PostMedia
        {
            Post = post,
            MediaUrl = upload.MediaUrl,
            ThumbnailUrl = upload.ThumbnailUrl
        };
        post.Media.Add(media);

        var responseHashtags = new List<Hashtag>(normalizedHashtags.Count);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddAsync(post, token);

            foreach (var name in normalizedHashtags)
            {
                if (!hashtagByName.TryGetValue(name, out var hashtag))
                {
                    hashtag = new Hashtag { Name = name };
                    await repository.AddHashtagAsync(hashtag, token);
                    hashtagByName[name] = hashtag;
                }

                hashtag.PostCount++;
                responseHashtags.Add(hashtag);
                await repository.AddPostHashtagAsync(new PostHashtag
                {
                    Post = post,
                    Hashtag = hashtag
                }, token);
            }
        }, cancellationToken);

        return new CreateReelResponse(
            post.Id,
            post.Content,
            post.PostType,
            post.Visibility,
            post.ReactionCount,
            post.CommentCount,
            post.ShareCount,
            post.SaveCount,
            post.ViewCount,
            post.CreatedAt,
            new PostFeedUserResponse(user.Id, user.DisplayName, user.AvatarUrl, user.IsVerified),
            post.Media.Select(item => new PostFeedMediaResponse(item.Id, item.MediaUrl, item.ThumbnailUrl)).ToList(),
            responseHashtags.Select(hashtag => new ReelHashtagResponse(hashtag.Id, hashtag.Name)).ToList(),
            false,
            false);
    }

    private static IReadOnlyList<string> NormalizeHashtags(IReadOnlyList<string> hashtags) =>
        hashtags
            .Select(hashtag => hashtag.Trim().TrimStart('#').Trim())
            .Where(hashtag => !string.IsNullOrWhiteSpace(hashtag))
            .Select(hashtag => hashtag.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
