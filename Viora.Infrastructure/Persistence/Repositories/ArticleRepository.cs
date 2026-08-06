using Microsoft.EntityFrameworkCore;
using Viora.Application.Articles;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class ArticleRepository(AppDbContext dbContext) : IArticleRepository
{
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(x => x.Id == userId, cancellationToken);

    public Task AddAsync(Post article, CancellationToken cancellationToken) =>
        dbContext.Posts.AddAsync(article, cancellationToken).AsTask();

    public Task<Post?> GetForUpdateAsync(Guid articleId, CancellationToken cancellationToken) =>
        dbContext.Posts.Include(x => x.ArticleBlocks).SingleOrDefaultAsync(x => x.Id == articleId, cancellationToken);

    public async Task PrepareBlockOrderUpdateAsync(Post article, CancellationToken cancellationToken)
    {
        var index = 1_000_000_000;
        foreach (var block in article.ArticleBlocks)
        {
            block.OrderIndex = index++;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordViewAsync(Guid userId, Guid articleId, CancellationToken cancellationToken)
    {
        await dbContext.Posts.Where(x => x.Id == articleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ViewCount, x => x.ViewCount + 1), cancellationToken);
        dbContext.ViewHistories.Add(new ViewHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PostId = articleId,
            WatchDuration = 0,
            IsCompleted = true,
            ViewedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<ArticleResponse>> GetAsync(Guid userId, Guid articleId, CancellationToken cancellationToken)
    {
        var access = await dbContext.Posts.AsNoTracking()
            .Where(x => x.Id == articleId && x.PostType == PostType.Article && x.DeletedAt == null)
            .Select(x => new
            {
                x.UserId,
                x.Status,
                x.Visibility,
                IsFollower = dbContext.Follows.Any(f => f.FollowerId == userId && f.FollowingId == x.UserId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (access is null || access.Status == PostStatus.Deleted)
            return Result<ArticleResponse>.Failure(PostInteractionError.NotFound, "Không tìm thấy bài viết dài.");

        var isOwner = access.UserId == userId;
        var canView = access.Status == PostStatus.Published &&
            (isOwner || access.Visibility == PostVisibility.Public ||
             (access.Visibility == PostVisibility.Followers && access.IsFollower));
        if (!canView)
            return Result<ArticleResponse>.Failure(PostInteractionError.Forbidden, "Bạn không có quyền xem bài viết này.");

        var data = await dbContext.Posts.AsNoTracking()
            .Where(x => x.Id == articleId)
            .Select(x => new
            {
                Post = x,
                Author = new ArticleAuthorResponse(x.User.Id, x.User.DisplayName, x.User.AvatarUrl, x.User.IsVerified),
                Blocks = x.ArticleBlocks.OrderBy(b => b.OrderIndex).Select(b => new ArticleBlockResponse(
                    b.Id, b.OrderIndex, b.BlockType, b.Content, b.MediaUrl, b.ThumbnailUrl,
                    b.Caption, b.CreatedAt, b.UpdatedAt)).ToList()
            })
            .SingleAsync(cancellationToken);

        var readable = data.Blocks.Where(x => x.Type is ArticleBlockType.Text or ArticleBlockType.Heading or ArticleBlockType.Quote or ArticleBlockType.Code);
        var thumbnail = data.Blocks.FirstOrDefault(x => x.Type == ArticleBlockType.Image)?.MediaUrl;
        var preview = data.Blocks.FirstOrDefault(x => x.Type == ArticleBlockType.Text)?.Content;
        if (preview?.Length > 200) preview = preview[..200].TrimEnd() + "…";

        return Result<ArticleResponse>.Success(new ArticleResponse(
            data.Post.Id,
            data.Post.Content ?? string.Empty,
            data.Post.Visibility,
            data.Post.Status,
            data.Post.CreatedAt,
            data.Post.UpdatedAt,
            ArticleReadingTime.Calculate(readable.Select(x => x.Content)),
            thumbnail,
            preview,
            data.Post.ReactionCount,
            data.Post.CommentCount,
            data.Post.ShareCount,
            data.Post.SaveCount,
            data.Post.ViewCount,
            isOwner,
            data.Author,
            data.Blocks));
    }
}
