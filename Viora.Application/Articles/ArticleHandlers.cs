using FluentValidation;
using MediatR;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Application.Articles;

public sealed class CreateArticleHandler(
    IArticleRepository repository,
    IUnitOfWork unitOfWork,
    IValidator<CreateArticleCommand> validator)
    : IRequestHandler<CreateArticleCommand, ArticleResponse>
{
    public async Task<ArticleResponse> Handle(CreateArticleCommand request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        if (!await repository.UserExistsAsync(request.UserId, cancellationToken))
        {
            throw new CreatePostException("USER_NOT_FOUND", "Không tìm thấy người dùng.");
        }

        var article = new Post
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Content = request.Title.Trim(),
            PostType = PostType.Article,
            Visibility = request.Visibility,
            Status = PostStatus.Published,
            ArticleBlocks = request.Blocks.Select(ToEntity).ToList()
        };

        await unitOfWork.ExecuteInTransactionAsync(
            token => repository.AddAsync(article, token), cancellationToken);

        var result = await repository.GetAsync(request.UserId, article.Id, cancellationToken);
        return result.Value ?? throw new InvalidOperationException("Article vừa tạo không thể được tải lại.");
    }

    private static ArticleBlock ToEntity(CreateArticleBlockRequest block) => new()
    {
        Id = Guid.NewGuid(),
        OrderIndex = block.OrderIndex,
        BlockType = block.Type,
        Content = block.Content?.Trim(),
        MediaUrl = block.MediaUrl?.Trim(),
        ThumbnailUrl = block.ThumbnailUrl?.Trim(),
        Caption = block.Caption?.Trim()
    };
}

public sealed class UpdateArticleHandler(
    IArticleRepository repository,
    IUnitOfWork unitOfWork,
    IValidator<UpdateArticleCommand> validator)
    : IRequestHandler<UpdateArticleCommand, Result<ArticleResponse>>
{
    public async Task<Result<ArticleResponse>> Handle(UpdateArticleCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<ArticleResponse>.Failure(PostInteractionError.Invalid,
                string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var article = await repository.GetForUpdateAsync(request.ArticleId, cancellationToken);
        if (article is null || article.PostType != PostType.Article || article.DeletedAt is not null)
        {
            return Result<ArticleResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bài viết dài.");
        }
        if (article.UserId != request.UserId)
        {
            return Result<ArticleResponse>.Failure(PostInteractionError.Forbidden, "Chỉ chủ bài viết được phép chỉnh sửa.");
        }

        var existingIds = article.ArticleBlocks.Select(x => x.Id).ToHashSet();
        if (request.Blocks.Any(x => x.Id.HasValue && !existingIds.Contains(x.Id.Value)))
        {
            return Result<ArticleResponse>.Failure(PostInteractionError.Invalid, "Block không thuộc bài viết này.");
        }

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            article.Content = request.Title.Trim();
            article.Visibility = request.Visibility;
            await repository.PrepareBlockOrderUpdateAsync(article, token);
            var requestedIds = request.Blocks.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();
            foreach (var removed in article.ArticleBlocks.Where(x => !requestedIds.Contains(x.Id)).ToArray())
            {
                article.ArticleBlocks.Remove(removed);
            }
            foreach (var block in request.Blocks)
            {
                var entity = block.Id.HasValue
                    ? article.ArticleBlocks.Single(x => x.Id == block.Id.Value)
                    : new ArticleBlock { Id = Guid.NewGuid(), PostId = article.Id };
                entity.OrderIndex = block.OrderIndex;
                entity.BlockType = block.Type;
                entity.Content = block.Content?.Trim();
                entity.MediaUrl = block.MediaUrl?.Trim();
                entity.ThumbnailUrl = block.ThumbnailUrl?.Trim();
                entity.Caption = block.Caption?.Trim();
                if (!block.Id.HasValue)
                {
                    article.ArticleBlocks.Add(entity);
                }
            }
        }, cancellationToken);

        return await repository.GetAsync(request.UserId, article.Id, cancellationToken);
    }
}

public sealed class GetArticleHandler(IArticleRepository repository)
    : IRequestHandler<GetArticleQuery, Result<ArticleResponse>>
{
    public async Task<Result<ArticleResponse>> Handle(GetArticleQuery request, CancellationToken cancellationToken)
    {
        var result = await repository.GetAsync(request.UserId, request.ArticleId, cancellationToken);
        if (!result.IsSuccess || result.Value is null) return result;
        await repository.RecordViewAsync(request.UserId, request.ArticleId, cancellationToken);
        return Result<ArticleResponse>.Success(result.Value with { ViewCount = result.Value.ViewCount + 1 });
    }
}
