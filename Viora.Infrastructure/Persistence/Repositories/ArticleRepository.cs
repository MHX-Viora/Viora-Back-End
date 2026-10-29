using Microsoft.EntityFrameworkCore;
using Viora.Application.Articles;
using Viora.Application.Posts;
using Viora.Domain.Entities;
using Viora.Application.Hashtags;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class ArticleRepository(AppDbContext dbContext) : IArticleRepository
{
    public Task<AccountStyle?> GetUserAccountStyleAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.Where(x => x.Id == userId)
            .Select(x => (AccountStyle?)x.AccountStyle)
            .SingleOrDefaultAsync(cancellationToken);

    public Task AddAsync(Post article, CancellationToken cancellationToken) =>
        dbContext.Posts.AddAsync(article, cancellationToken).AsTask();

    public Task<Post?> GetForUpdateAsync(Guid articleId, CancellationToken cancellationToken) =>
        dbContext.Posts.Include(x => x.ArticleBlocks).SingleOrDefaultAsync(x => x.Id == articleId, cancellationToken);

    public async Task SetHashtagsAsync(Post article, IReadOnlyList<string> hashtags, CancellationToken cancellationToken)
    {
        var names = hashtags.Select(HashtagNormalizer.Normalize).Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var links = await dbContext.PostHashtags.Include(link => link.Hashtag)
            .Where(link => link.PostId == article.Id).ToListAsync(cancellationToken);
        foreach (var removed in links.Where(link => !names.Contains(link.Hashtag.Name)))
        {
            removed.Hashtag.PostCount = Math.Max(0, removed.Hashtag.PostCount - 1);
            dbContext.PostHashtags.Remove(removed);
        }
        var added = names.Except(links.Select(link => link.Hashtag.Name), StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await dbContext.Hashtags.Where(tag => added.Contains(tag.Name)).ToListAsync(cancellationToken);
        foreach (var name in added)
        {
            var tag = existing.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tag is null) { tag = new Hashtag { Name = name }; dbContext.Hashtags.Add(tag); }
            tag.PostCount++;
            dbContext.PostHashtags.Add(new PostHashtag { Post = article, Hashtag = tag });
        }
    }

    public async Task PrepareBlockOrderUpdateAsync(Post article, CancellationToken cancellationToken)
    {
        var index = 1_000_000_000;
        foreach (var block in article.ArticleBlocks)
        {
            block.OrderIndex = index++;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordViewAsync(Guid userId, Guid articleId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var interactionId = Guid.NewGuid();
        var interactionType = (short)ArticleInteractionType.View;
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ArticleInteractions"
                ("Id", "UserId", "ArticleId", "InteractionType", "ReadDuration", "ReadPercentage", "CreatedAt", "UpdatedAt")
            VALUES
                ({interactionId}, {userId}, {articleId}, {interactionType}, 0, 0, {now}, {now})
            ON CONFLICT ("UserId", "ArticleId", "InteractionType") DO NOTHING
            """, cancellationToken);

        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await dbContext.Posts.Where(x => x.Id == articleId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ViewCount, x => x.ViewCount + 1),
                cancellationToken);
        dbContext.ViewHistories.Add(new ViewHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PostId = articleId,
            WatchDuration = 0,
            IsCompleted = true,
            ViewedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
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
                Author = new ArticleAuthorResponse(x.User.Id, x.User.DisplayName, x.User.AvatarUrl, x.User.IsVerified, x.User.AccountStyle),
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
            data.Blocks)
        {
            Hashtags = await dbContext.PostHashtags.AsNoTracking().Where(link => link.PostId == articleId)
                .OrderBy(link => link.Hashtag.Name)
                .Select(link => new PostDetailHashtagResponse(link.Hashtag.Id, link.Hashtag.Name)).ToListAsync(cancellationToken)
        });
    }
}
