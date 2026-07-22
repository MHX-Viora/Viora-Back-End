using FluentValidation;
using MediatR;
using Viora.Application.Notifications;
using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed class ReactPostHandler(
    IPostInteractionRepository repository,
    IValidator<ReactPostCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<ReactPostCommand, Result<PostReactionResponse>>
{
    public async Task<Result<PostReactionResponse>> Handle(ReactPostCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<PostReactionResponse>.Failure(PostInteractionError.Invalid, FirstError(validation));
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var post = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        var valid = await GuardPostAsync(repository, user, post, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<PostReactionResponse>.Failure(valid.Error!.Value, valid.Message!);

        PostReactionResponse response = null!;
        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            var reaction = await repository.GetReactionAsync(request.PostId, request.UserId, token);
            if (reaction is null)
            {
                await repository.AddReactionAsync(new PostReaction
                {
                    PostId = request.PostId,
                    UserId = request.UserId,
                    ReactionType = request.ReactionType,
                    CreatedAt = DateTime.UtcNow
                }, token);
                post!.ReactionCount++;
                notification = await AddPostNotificationAsync(repository, post, user!, NotificationType.PostLike, post.Id, token);
                response = new PostReactionResponse(request.ReactionType, post.ReactionCount);
                return;
            }

            if (reaction.ReactionType == request.ReactionType)
            {
                repository.RemoveReaction(reaction);
                post!.ReactionCount = Math.Max(0, post.ReactionCount - 1);
                response = new PostReactionResponse(null, post.ReactionCount);
                return;
            }

            reaction.ReactionType = request.ReactionType;
            response = new PostReactionResponse(request.ReactionType, post!.ReactionCount);
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<PostReactionResponse>.Success(response);
    }

    internal static async Task<Result<EmptyResponse>> GuardPostAsync(
        IPostInteractionRepository repository,
        User? user,
        Post? post,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (user is null) return Result<EmptyResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy người dùng.");
        if (post is null) return Result<EmptyResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bài viết.");
        if (post.Status is PostStatus.Deleted or PostStatus.Hidden || post.DeletedAt is not null)
        {
            return Result<EmptyResponse>.Failure(PostInteractionError.Invalid, "Bài viết không khả dụng.");
        }
        if (!await repository.CanViewPostAsync(post, userId, cancellationToken))
        {
            return Result<EmptyResponse>.Failure(PostInteractionError.Forbidden, "Bạn không có quyền xem bài viết.");
        }
        return Result<EmptyResponse>.Success(new EmptyResponse());
    }

    internal static async Task<Notification?> AddPostNotificationAsync(
        IPostInteractionRepository repository,
        Post post,
        User sender,
        NotificationType type,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        if (post.UserId == sender.Id) return null;
        var notification = NotificationFactory.Create(
            post.UserId,
            type,
            sender,
            referenceId,
            type == NotificationType.CommentReply
                ? NotificationReferenceType.Comment
                : NotificationReferenceType.Post,
            post.PostType);
        await repository.AddNotificationAsync(notification, cancellationToken);
        return notification;
    }

    internal static CommentResponse MapComment(Comment comment) => new(
        comment.Id,
        new PostInteractionUserResponse(
            comment.User.Id,
            comment.User.DisplayName,
            comment.User.AvatarUrl,
            comment.User.IsVerified),
        comment.Content,
        comment.CreatedAt,
        comment.ReplyCount,
        comment.LikeCount);

    internal static string FirstError(FluentValidation.Results.ValidationResult validation) =>
        validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
}

public sealed class CreateCommentHandler(
    IPostInteractionRepository repository,
    IValidator<CreateCommentCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<CreateCommentCommand, Result<CommentResponse>>
{
    public async Task<Result<CommentResponse>> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<CommentResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var post = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        var valid = await ReactPostHandler.GuardPostAsync(repository, user, post, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<CommentResponse>.Failure(valid.Error!.Value, valid.Message!);

        var comment = new Comment
        {
            PostId = request.PostId,
            UserId = request.UserId,
            User = user!,
            Content = request.Content.Trim(),
            Status = CommentStatus.Published
        };

        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddCommentAsync(comment, token);
            post!.CommentCount++;
            notification = await ReactPostHandler.AddPostNotificationAsync(repository, post, user!, NotificationType.PostComment, post.Id, token);
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<CommentResponse>.Success(ReactPostHandler.MapComment(comment));
    }
}

public sealed class ReplyCommentHandler(
    IPostInteractionRepository repository,
    IValidator<ReplyCommentCommand> validator,
    INotificationService notificationService)
    : IRequestHandler<ReplyCommentCommand, Result<CommentReplyListItemResponse>>
{
    public async Task<Result<CommentReplyListItemResponse>> Handle(ReplyCommentCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<CommentReplyListItemResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var parent = await repository.GetCommentForReplyAsync(request.CommentId, cancellationToken);
        if (user is null) return Result<CommentReplyListItemResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy người dùng.");
        if (parent is null) return Result<CommentReplyListItemResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bình luận.");
        if (parent.ParentCommentId is not null) return Result<CommentReplyListItemResponse>.Failure(PostInteractionError.Invalid, "Chỉ hỗ trợ reply tối đa 1 tầng.");
        if (parent.Post.Status is PostStatus.Deleted or PostStatus.Hidden || parent.Post.DeletedAt is not null)
        {
            return Result<CommentReplyListItemResponse>.Failure(PostInteractionError.Invalid, "Bài viết không khả dụng.");
        }
        if (!await repository.CanViewPostAsync(parent.Post, request.UserId, cancellationToken))
        {
            return Result<CommentReplyListItemResponse>.Failure(PostInteractionError.Forbidden, "Bạn không có quyền xem bài viết.");
        }

        var reply = new Comment
        {
            PostId = parent.PostId,
            UserId = request.UserId,
            User = user,
            ParentCommentId = parent.Id,
            ReplyToUserId = parent.UserId,
            ReplyToUser = parent.User,
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
                    parent.PostId,
                    NotificationReferenceType.Comment,
                    parent.Post.PostType);
                await repository.AddNotificationAsync(notification, token);
            }
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<CommentReplyListItemResponse>.Success(new CommentReplyListItemResponse(
            reply.Id,
            reply.Content,
            reply.CreatedAt,
            reply.UpdatedAt,
            reply.LikeCount,
            false,
            new CommentReplyToUserResponse(parent.User.Id, parent.User.DisplayName),
            new PostInteractionUserResponse(user.Id, user.DisplayName, user.AvatarUrl, user.IsVerified)));
    }
}

public sealed class ToggleCommentLikeHandler(
    IPostInteractionRepository repository,
    IValidator<ToggleCommentLikeCommand> validator,
    INotificationService notificationService,
    IRealtimeService realtimeService)
    : IRequestHandler<ToggleCommentLikeCommand, Result<CommentLikeResponse>>
{
    public async Task<Result<CommentLikeResponse>> Handle(ToggleCommentLikeCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<CommentLikeResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        if (user is null) return Result<CommentLikeResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy người dùng.");

        var comment = await repository.GetCommentForLikeAsync(request.CommentId, cancellationToken);
        if (comment is null) return Result<CommentLikeResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bình luận.");
        if (comment.Status != CommentStatus.Published || comment.DeletedAt is not null)
        {
            return Result<CommentLikeResponse>.Failure(PostInteractionError.Invalid, "Không thể thích bình luận này.");
        }

        CommentLikeResponse response = null!;
        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            var reaction = await repository.GetCommentReactionAsync(request.CommentId, request.UserId, token);
            if (reaction is null)
            {
                await repository.AddCommentReactionAsync(new CommentReaction
                {
                    CommentId = request.CommentId,
                    UserId = request.UserId,
                    ReactionType = ReactionType.Like,
                    CreatedAt = DateTime.UtcNow
                }, token);
                comment.LikeCount++;
                response = new CommentLikeResponse(comment.Id, true, comment.LikeCount);

                if (comment.UserId != request.UserId)
                {
                    notification = new Notification
                    {
                        UserId = comment.UserId,
                        SenderUserId = user.Id,
                        SenderUser = user,
                        NotificationType = NotificationType.CommentLike,
                        Title = "Có người đã thích bình luận của bạn.",
                        Content = $"{user.DisplayName} đã thích bình luận của bạn.",
                        ReferenceType = NotificationReferenceType.Comment,
                        ReferenceId = comment.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    await repository.AddNotificationAsync(notification, token);
                }

                return;
            }

            repository.RemoveCommentReaction(reaction);
            comment.LikeCount = Math.Max(0, comment.LikeCount - 1);
            response = new CommentLikeResponse(comment.Id, false, comment.LikeCount);
        }, cancellationToken);

        await realtimeService.SendToUsersAsync(
            new[] { request.UserId, comment.UserId },
            RealtimeEvents.ReceiveCommentLike,
            new CommentLikeRealtimePayload(response.CommentId, response.LikeCount, response.IsLiked, request.UserId),
            cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<CommentLikeResponse>.Success(response);
    }
}

public sealed class ToggleSavePostHandler(IPostInteractionRepository repository)
    : IRequestHandler<ToggleSavePostCommand, Result<SavePostResponse>>
{
    public async Task<Result<SavePostResponse>> Handle(ToggleSavePostCommand request, CancellationToken cancellationToken)
    {
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var post = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        var valid = await ReactPostHandler.GuardPostAsync(repository, user, post, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<SavePostResponse>.Failure(valid.Error!.Value, valid.Message!);

        SavePostResponse response = null!;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            var saved = await repository.GetSavedPostAsync(request.PostId, request.UserId, token);
            if (saved is null)
            {
                await repository.AddSavedPostAsync(new SavedPost { PostId = request.PostId, UserId = request.UserId }, token);
                post!.SaveCount++;
                response = new SavePostResponse(true, post.SaveCount);
                return;
            }
            repository.RemoveSavedPost(saved);
            post!.SaveCount = Math.Max(0, post.SaveCount - 1);
            response = new SavePostResponse(false, post.SaveCount);
        }, cancellationToken);

        return Result<SavePostResponse>.Success(response);
    }
}

public sealed class SharePostHandler(
    IPostInteractionRepository repository,
    INotificationService notificationService)
    : IRequestHandler<SharePostCommand, Result<SharePostResponse>>
{
    public async Task<Result<SharePostResponse>> Handle(SharePostCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || request.PostId == Guid.Empty)
        {
            return Result<SharePostResponse>.Failure(PostInteractionError.Invalid, "Dữ liệu không hợp lệ.");
        }

        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var original = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        var valid = await ReactPostHandler.GuardPostAsync(repository, user, original, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<SharePostResponse>.Failure(valid.Error!.Value, valid.Message!);
        var post = original!;

        Notification? notification = null;
        await repository.ExecuteInTransactionAsync(async token =>
        {
            post.ShareCount++;
            notification = await ReactPostHandler.AddPostNotificationAsync(repository, post, user!, NotificationType.PostShare, post.Id, token);
        }, cancellationToken);

        if (notification is not null)
        {
            await notificationService.PublishAsync(notification, cancellationToken);
        }

        return Result<SharePostResponse>.Success(new SharePostResponse(true, post.ShareCount));
    }
}

public sealed class DeletePostHandler(IPostInteractionRepository repository)
    : IRequestHandler<DeletePostCommand, Result<EmptyResponse>>
{
    public async Task<Result<EmptyResponse>> Handle(DeletePostCommand request, CancellationToken cancellationToken)
    {
        var post = await repository.GetPostWithOriginalAsync(request.PostId, cancellationToken);
        if (post is null) return Result<EmptyResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bài viết.");
        if (post.UserId != request.UserId) return Result<EmptyResponse>.Failure(PostInteractionError.Forbidden, "Bạn không có quyền xóa bài viết.");
        if (post.Status == PostStatus.Deleted) return Result<EmptyResponse>.Success(new EmptyResponse());

        post.Status = PostStatus.Deleted;
        post.DeletedAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return Result<EmptyResponse>.Success(new EmptyResponse());
    }
}

public sealed class ReportPostHandler(
    IPostInteractionRepository repository,
    IValidator<ReportPostCommand> validator)
    : IRequestHandler<ReportPostCommand, Result<ReportPostResponse>>
{
    public async Task<Result<ReportPostResponse>> Handle(ReportPostCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<ReportPostResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var post = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        var valid = await ReactPostHandler.GuardPostAsync(repository, user, post, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<ReportPostResponse>.Failure(valid.Error!.Value, valid.Message!);
        if (post!.UserId == request.UserId) return Result<ReportPostResponse>.Failure(PostInteractionError.Invalid, "Không thể báo cáo bài viết của chính mình.");
        if (await repository.HasReportedPostAsync(request.PostId, request.UserId, cancellationToken))
        {
            return Result<ReportPostResponse>.Failure(PostInteractionError.Conflict, "Bạn đã báo cáo bài viết này.");
        }

        var report = new Report
        {
            ReporterUserId = request.UserId,
            TargetId = request.PostId,
            TargetType = ReportTargetType.Post,
            Reason = request.Reason,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = ReportStatus.Pending
        };
        await repository.AddReportAsync(report, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result<ReportPostResponse>.Success(new ReportPostResponse(report.Id, "Báo cáo bài viết thành công."));
    }
}

public sealed class GetPostCommentsHandler(
    IPostInteractionRepository repository,
    IValidator<GetPostCommentsQuery> validator)
    : IRequestHandler<GetPostCommentsQuery, Result<PostCommentsResponse>>
{
    public async Task<Result<PostCommentsResponse>> Handle(GetPostCommentsQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<PostCommentsResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var post = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        if (post is null) return Result<PostCommentsResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay bai viet.");

        var sort = string.Equals(request.Sort, "oldest", StringComparison.OrdinalIgnoreCase)
            ? "oldest"
            : "newest";

        var response = await repository.GetPostCommentsAsync(request with { Sort = sort }, cancellationToken);
        return Result<PostCommentsResponse>.Success(response);
    }
}

public sealed class GetCommentRepliesHandler(
    IPostInteractionRepository repository,
    IValidator<GetCommentRepliesQuery> validator)
    : IRequestHandler<GetCommentRepliesQuery, Result<CommentRepliesResponse>>
{
    public async Task<Result<CommentRepliesResponse>> Handle(GetCommentRepliesQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<CommentRepliesResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));

        var comment = await repository.GetCommentForReplyAsync(request.CommentId, cancellationToken);
        if (comment is null) return Result<CommentRepliesResponse>.Failure(PostInteractionError.NotFound, "Khong tim thay binh luan.");

        var sort = string.Equals(request.Sort, "newest", StringComparison.OrdinalIgnoreCase)
            ? "newest"
            : "oldest";

        var response = await repository.GetCommentRepliesAsync(request with { Sort = sort }, cancellationToken);
        return Result<CommentRepliesResponse>.Success(response);
    }
}
