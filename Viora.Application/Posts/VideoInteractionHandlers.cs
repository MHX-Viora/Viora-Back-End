using FluentValidation;
using MediatR;
using Viora.Application.Notifications;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

internal static class VideoInteractionGuard
{
    internal static async Task<Result<EmptyResponse>> ValidateVideoAsync(
        IPostInteractionRepository repository,
        User? user,
        Post? video,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var valid = await ReactPostHandler.GuardPostAsync(repository, user, video, userId, cancellationToken);
        if (!valid.IsSuccess)
        {
            return valid;
        }

        return video!.PostType == PostType.ShortVideo
            ? Result<EmptyResponse>.Success(new EmptyResponse())
            : Result<EmptyResponse>.Failure(PostInteractionError.Invalid, "Video khong hop le.");
    }

    internal static VideoCommentResponse MapComment(Comment comment) => new(
        comment.Id,
        comment.Content,
        comment.CreatedAt,
        comment.LikeCount,
        comment.ReplyCount,
        new PostInteractionUserResponse(
            comment.User.Id,
            comment.User.DisplayName,
            comment.User.AvatarUrl,
            comment.User.IsVerified));

    internal static VideoReplyResponse MapReply(Comment comment) => new(
        comment.Id,
        comment.Content,
        comment.CreatedAt,
        comment.LikeCount,
        comment.ReplyCount,
        new PostInteractionUserResponse(
            comment.User.Id,
            comment.User.DisplayName,
            comment.User.AvatarUrl,
            comment.User.IsVerified));
}

public sealed class ToggleVideoReactionHandler(
    IPostInteractionRepository repository,
    IValidator<ToggleVideoReactionCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<ToggleVideoReactionCommand, Result<VideoReactionResponse>>
{
    public async Task<Result<VideoReactionResponse>> Handle(ToggleVideoReactionCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<VideoReactionResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var video = await repository.GetPostForInteractionAsync(request.VideoId, cancellationToken);
        var valid = await VideoInteractionGuard.ValidateVideoAsync(repository, user, video, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<VideoReactionResponse>.Failure(valid.Error!.Value, valid.Message!);

        VideoReactionResponse response = null!;
        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            var reaction = await repository.GetReactionAsync(request.VideoId, request.UserId, token);
            if (reaction is null)
            {
                await repository.AddReactionAsync(new PostReaction
                {
                    PostId = request.VideoId,
                    UserId = request.UserId,
                    ReactionType = ReactionType.Love,
                    CreatedAt = DateTime.UtcNow
                }, token);
                video!.ReactionCount++;
                notification = await ReactPostHandler.AddPostNotificationAsync(repository, video, user!, NotificationType.PostLike, video.Id, token);
                response = new VideoReactionResponse(true, video.ReactionCount);
                return;
            }

            repository.RemoveReaction(reaction);
            video!.ReactionCount = Math.Max(0, video.ReactionCount - 1);
            response = new VideoReactionResponse(false, video.ReactionCount);
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<VideoReactionResponse>.Success(response);
    }
}

public sealed class ToggleVideoSaveHandler(
    IPostInteractionRepository repository,
    IValidator<ToggleVideoSaveCommand> validator)
    : IRequestHandler<ToggleVideoSaveCommand, Result<SavePostResponse>>
{
    public async Task<Result<SavePostResponse>> Handle(ToggleVideoSaveCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<SavePostResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var video = await repository.GetPostForInteractionAsync(request.VideoId, cancellationToken);
        var valid = await VideoInteractionGuard.ValidateVideoAsync(repository, user, video, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<SavePostResponse>.Failure(valid.Error!.Value, valid.Message!);

        SavePostResponse response = null!;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            var saved = await repository.GetSavedPostAsync(request.VideoId, request.UserId, token);
            if (saved is null)
            {
                await repository.AddSavedPostAsync(new SavedPost { PostId = request.VideoId, UserId = request.UserId }, token);
                video!.SaveCount++;
                response = new SavePostResponse(true, video.SaveCount);
                return;
            }

            repository.RemoveSavedPost(saved);
            video!.SaveCount = Math.Max(0, video.SaveCount - 1);
            response = new SavePostResponse(false, video.SaveCount);
        }, cancellationToken);

        return Result<SavePostResponse>.Success(response);
    }
}

public sealed class ShareVideoHandler(
    IPostInteractionRepository repository,
    IValidator<ShareVideoCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<ShareVideoCommand, Result<VideoShareResponse>>
{
    public async Task<Result<VideoShareResponse>> Handle(ShareVideoCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<VideoShareResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var video = await repository.GetPostForInteractionAsync(request.VideoId, cancellationToken);
        var valid = await VideoInteractionGuard.ValidateVideoAsync(repository, user, video, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<VideoShareResponse>.Failure(valid.Error!.Value, valid.Message!);

        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            video!.ShareCount++;
            notification = await ReactPostHandler.AddPostNotificationAsync(repository, video, user!, NotificationType.PostShare, video.Id, token);
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<VideoShareResponse>.Success(new VideoShareResponse(video!.ShareCount));
    }
}

public sealed class CreateVideoCommentHandler(
    IPostInteractionRepository repository,
    IValidator<CreateVideoCommentCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<CreateVideoCommentCommand, Result<VideoCommentResponse>>
{
    public async Task<Result<VideoCommentResponse>> Handle(CreateVideoCommentCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<VideoCommentResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var video = await repository.GetPostForInteractionAsync(request.VideoId, cancellationToken);
        var valid = await VideoInteractionGuard.ValidateVideoAsync(repository, user, video, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<VideoCommentResponse>.Failure(valid.Error!.Value, valid.Message!);

        var comment = new Comment
        {
            PostId = request.VideoId,
            UserId = request.UserId,
            User = user!,
            Content = request.Content.Trim(),
            Status = CommentStatus.Published
        };

        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddCommentAsync(comment, token);
            video!.CommentCount++;
            notification = await ReactPostHandler.AddPostNotificationAsync(repository, video, user!, NotificationType.PostComment, video.Id, token);
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<VideoCommentResponse>.Success(VideoInteractionGuard.MapComment(comment));
    }
}

public sealed class ReplyVideoCommentHandler(
    IPostInteractionRepository repository,
    IValidator<ReplyVideoCommentCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<ReplyVideoCommentCommand, Result<VideoReplyResponse>>
{
    public async Task<Result<VideoReplyResponse>> Handle(ReplyVideoCommentCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<VideoReplyResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var parent = await repository.GetCommentForReplyAsync(request.CommentId, cancellationToken);
        if (user is null) return Result<VideoReplyResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay nguoi dung.");
        if (parent is null) return Result<VideoReplyResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay binh luan.");
        if (parent.ParentCommentId is not null) return Result<VideoReplyResponse>.Failure(PostInteractionError.Invalid, "Chi ho tro reply comment goc.");
        if (parent.Post.PostType != PostType.ShortVideo) return Result<VideoReplyResponse>.Failure(PostInteractionError.Invalid, "Video khong hop le.");

        var valid = await ReactPostHandler.GuardPostAsync(repository, user, parent.Post, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<VideoReplyResponse>.Failure(valid.Error!.Value, valid.Message!);

        var reply = new Comment
        {
            PostId = parent.PostId,
            UserId = request.UserId,
            User = user,
            ParentCommentId = parent.Id,
            ReplyToUserId = parent.UserId,
            Content = request.Content.Trim(),
            Status = CommentStatus.Published
        };

        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddCommentAsync(reply, token);
            parent.ReplyCount++;
            if (parent.UserId != request.UserId)
            {
                notification = NotificationFactory.Create(
                    parent.UserId,
                    NotificationType.CommentReply,
                    user,
                    parent.Id,
                    NotificationReferenceType.Comment,
                    parent.Post.PostType);
                await repository.AddNotificationAsync(notification, token);
            }
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<VideoReplyResponse>.Success(VideoInteractionGuard.MapReply(reply));
    }
}

public sealed class GetVideoCommentsHandler(
    IPostInteractionRepository repository,
    IValidator<GetVideoCommentsQuery> validator)
    : IRequestHandler<GetVideoCommentsQuery, Result<VideoCommentsResponse>>
{
    public async Task<Result<VideoCommentsResponse>> Handle(GetVideoCommentsQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<VideoCommentsResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var video = await repository.GetPostForInteractionAsync(request.VideoId, cancellationToken);
        var valid = await VideoInteractionGuard.ValidateVideoAsync(repository, user, video, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<VideoCommentsResponse>.Failure(valid.Error!.Value, valid.Message!);

        return Result<VideoCommentsResponse>.Success(await repository.GetVideoCommentsAsync(request, cancellationToken));
    }
}

public sealed class GetVideoRepliesHandler(
    IPostInteractionRepository repository,
    IValidator<GetVideoRepliesQuery> validator)
    : IRequestHandler<GetVideoRepliesQuery, Result<VideoRepliesResponse>>
{
    public async Task<Result<VideoRepliesResponse>> Handle(GetVideoRepliesQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<VideoRepliesResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var parent = await repository.GetCommentForReplyAsync(request.CommentId, cancellationToken);
        if (user is null) return Result<VideoRepliesResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay nguoi dung.");
        if (parent is null) return Result<VideoRepliesResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay binh luan.");
        if (parent.ParentCommentId is not null) return Result<VideoRepliesResponse>.Failure(PostInteractionError.Invalid, "Chi lay reply cua comment goc.");
        if (parent.Post.PostType != PostType.ShortVideo) return Result<VideoRepliesResponse>.Failure(PostInteractionError.Invalid, "Video khong hop le.");

        var valid = await ReactPostHandler.GuardPostAsync(repository, user, parent.Post, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<VideoRepliesResponse>.Failure(valid.Error!.Value, valid.Message!);

        return Result<VideoRepliesResponse>.Success(await repository.GetVideoRepliesAsync(request, cancellationToken));
    }
}

public sealed class DeleteVideoCommentHandler(
    IPostInteractionRepository repository,
    IValidator<DeleteVideoCommentCommand> validator)
    : IRequestHandler<DeleteVideoCommentCommand, Result<EmptyResponse>>
{
    public async Task<Result<EmptyResponse>> Handle(DeleteVideoCommentCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<EmptyResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var comment = await repository.GetCommentForDeleteAsync(request.CommentId, cancellationToken);
        if (comment is null) return Result<EmptyResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay binh luan.");
        if (comment.Post.PostType != PostType.ShortVideo) return Result<EmptyResponse>.Failure(PostInteractionError.Invalid, "Video khong hop le.");
        if (!request.IsAdmin && comment.UserId != request.UserId)
        {
            return Result<EmptyResponse>.Failure(PostInteractionError.Forbidden, "Ban khong co quyen xoa binh luan.");
        }

        await repository.ExecuteInTransactionAsync(_ =>
        {
            SoftDeleteComment(comment);
            return Task.CompletedTask;
        }, cancellationToken);

        return Result<EmptyResponse>.Success(new EmptyResponse());
    }

    private static void SoftDeleteComment(Comment comment)
    {
        var now = DateTime.UtcNow;
        if (comment.Status == CommentStatus.Deleted || comment.DeletedAt is not null)
        {
            return;
        }

        comment.Status = CommentStatus.Deleted;
        comment.DeletedAt = now;

        if (comment.ParentCommentId is null)
        {
            comment.Post.CommentCount = Math.Max(0, comment.Post.CommentCount - 1);
            foreach (var reply in comment.Replies.Where(reply => reply.Status != CommentStatus.Deleted && reply.DeletedAt is null))
            {
                reply.Status = CommentStatus.Deleted;
                reply.DeletedAt = now;
            }
            comment.ReplyCount = 0;
            return;
        }

        if (comment.ParentComment is not null)
        {
            comment.ParentComment.ReplyCount = Math.Max(0, comment.ParentComment.ReplyCount - 1);
        }
    }
}
