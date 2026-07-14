using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public sealed class ReactPostHandler(
    IPostInteractionRepository repository,
    IValidator<ReactPostCommand> validator)
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
                await AddPostNotificationAsync(repository, post, request.UserId, NotificationType.PostLike, "Có người đã bày tỏ cảm xúc bài viết của bạn.", token);
                response = new PostReactionResponse(true, request.ReactionType, post.ReactionCount);
                return;
            }

            if (reaction.ReactionType == request.ReactionType)
            {
                repository.RemoveReaction(reaction);
                post!.ReactionCount = Math.Max(0, post.ReactionCount - 1);
                response = new PostReactionResponse(false, null, post.ReactionCount);
                return;
            }

            reaction.ReactionType = request.ReactionType;
            response = new PostReactionResponse(true, request.ReactionType, post!.ReactionCount);
        }, cancellationToken);

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

    internal static async Task AddPostNotificationAsync(
        IPostInteractionRepository repository,
        Post post,
        Guid senderUserId,
        NotificationType type,
        string title,
        CancellationToken cancellationToken)
    {
        if (post.UserId == senderUserId) return;
        await repository.AddNotificationAsync(new Notification
        {
            UserId = post.UserId,
            SenderUserId = senderUserId,
            NotificationType = type,
            ReferenceId = post.Id,
            ReferenceType = NotificationReferenceType.Post,
            Title = title
        }, cancellationToken);
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
    IValidator<CreateCommentCommand> validator)
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

        await repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddCommentAsync(comment, token);
            post!.CommentCount++;
            await ReactPostHandler.AddPostNotificationAsync(repository, post, request.UserId, NotificationType.PostComment, "Có bình luận mới trên bài viết của bạn.", token);
        }, cancellationToken);

        return Result<CommentResponse>.Success(ReactPostHandler.MapComment(comment));
    }
}

public sealed class ReplyCommentHandler(
    IPostInteractionRepository repository,
    IValidator<ReplyCommentCommand> validator)
    : IRequestHandler<ReplyCommentCommand, Result<CommentResponse>>
{
    public async Task<Result<CommentResponse>> Handle(ReplyCommentCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<CommentResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var parent = await repository.GetCommentForReplyAsync(request.CommentId, cancellationToken);
        if (user is null) return Result<CommentResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy người dùng.");
        if (parent is null) return Result<CommentResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bình luận.");
        if (parent.ParentCommentId is not null) return Result<CommentResponse>.Failure(PostInteractionError.Invalid, "Chỉ hỗ trợ reply tối đa 1 tầng.");
        if (parent.Post.Status is PostStatus.Deleted or PostStatus.Hidden || parent.Post.DeletedAt is not null)
        {
            return Result<CommentResponse>.Failure(PostInteractionError.Invalid, "Bài viết không khả dụng.");
        }
        if (!await repository.CanViewPostAsync(parent.Post, request.UserId, cancellationToken))
        {
            return Result<CommentResponse>.Failure(PostInteractionError.Forbidden, "Bạn không có quyền xem bài viết.");
        }

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

        await repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddCommentAsync(reply, token);
            parent.ReplyCount++;
            if (parent.UserId != request.UserId)
            {
                await repository.AddNotificationAsync(new Notification
                {
                    UserId = parent.UserId,
                    SenderUserId = request.UserId,
                    NotificationType = NotificationType.CommentReply,
                    ReferenceId = parent.Id,
                    ReferenceType = NotificationReferenceType.Comment,
                    Title = "Có phản hồi mới cho bình luận của bạn."
                }, token);
            }
        }, cancellationToken);

        return Result<CommentResponse>.Success(ReactPostHandler.MapComment(reply));
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
    IValidator<SharePostCommand> validator)
    : IRequestHandler<SharePostCommand, Result<SharePostResponse>>
{
    public async Task<Result<SharePostResponse>> Handle(SharePostCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<SharePostResponse>.Failure(PostInteractionError.Invalid, ReactPostHandler.FirstError(validation));
        var user = await repository.GetActiveUserAsync(request.UserId, cancellationToken);
        var original = await repository.GetPostForInteractionAsync(request.PostId, cancellationToken);
        var valid = await ReactPostHandler.GuardPostAsync(repository, user, original, request.UserId, cancellationToken);
        if (!valid.IsSuccess) return Result<SharePostResponse>.Failure(valid.Error!.Value, valid.Message!);

        var share = new Post
        {
            UserId = request.UserId,
            User = user!,
            OriginalPostId = original!.Id,
            Content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim(),
            PostType = original.PostType,
            Visibility = PostVisibility.Public,
            Status = PostStatus.Published
        };

        await repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddPostAsync(share, token);
            original.ShareCount++;
            await ReactPostHandler.AddPostNotificationAsync(repository, original, request.UserId, NotificationType.PostShare, "Bài viết của bạn đã được chia sẻ.", token);
        }, cancellationToken);

        return Result<SharePostResponse>.Success(new SharePostResponse(
            share.Id,
            share.Content,
            share.PostType,
            share.Visibility,
            string.IsNullOrWhiteSpace(share.Link) ? null : share.Link,
            original.Id,
            share.ReactionCount,
            share.CommentCount,
            share.ShareCount,
            share.SaveCount,
            share.ViewCount,
            share.CreatedAt));
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
        return Result<ReportPostResponse>.Success(new ReportPostResponse(report.Id));
    }
}
