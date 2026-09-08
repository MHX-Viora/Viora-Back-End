using Microsoft.EntityFrameworkCore;
using Viora.Application.Articles;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class ArticleInteractionRepository(AppDbContext dbContext)
    : IArticleInteractionRepository
{
    public async Task<Result<ArticleInteractionResponse>> UpsertAsync(
        RecordArticleInteractionCommand command,
        CancellationToken cancellationToken)
    {
        var access = await dbContext.Posts
            .AsNoTracking()
            .Where(post => post.Id == command.ArticleId &&
                post.PostType == PostType.Article &&
                post.Status == PostStatus.Published &&
                post.DeletedAt == null)
            .Select(post => new
            {
                post.UserId,
                post.Visibility,
                IsFollower = dbContext.Follows.Any(follow =>
                    follow.FollowerId == command.UserId &&
                    follow.FollowingId == post.UserId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (access is null)
            return Result<ArticleInteractionResponse>.Failure(
                PostInteractionError.NotFound,
                "Không tìm thấy bài báo.");

        var canView = access.UserId == command.UserId ||
            access.Visibility == PostVisibility.Public ||
            (access.Visibility == PostVisibility.Followers && access.IsFollower);
        if (!canView)
            return Result<ArticleInteractionResponse>.Failure(
                PostInteractionError.Forbidden,
                "Bạn không có quyền xem bài báo này.");

        var now = DateTime.UtcNow;
        var interactionId = Guid.NewGuid();
        var interactionType = (short)command.InteractionType;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ArticleInteractions"
                ("Id", "UserId", "ArticleId", "InteractionType", "ReadDuration", "ReadPercentage", "CreatedAt", "UpdatedAt")
            VALUES
                ({interactionId}, {command.UserId}, {command.ArticleId}, {interactionType},
                 {command.ReadDuration}, {command.ReadPercentage}, {now}, {now})
            ON CONFLICT ("UserId", "ArticleId", "InteractionType") DO UPDATE SET
                "ReadDuration" = GREATEST("ArticleInteractions"."ReadDuration", EXCLUDED."ReadDuration"),
                "ReadPercentage" = GREATEST("ArticleInteractions"."ReadPercentage", EXCLUDED."ReadPercentage"),
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);

        var stored = await dbContext.ArticleInteractions
            .AsNoTracking()
            .Where(interaction =>
                interaction.UserId == command.UserId &&
                interaction.ArticleId == command.ArticleId &&
                interaction.InteractionType == command.InteractionType)
            .Select(interaction => new ArticleInteractionResponse(
                interaction.ArticleId,
                interaction.InteractionType,
                interaction.ReadDuration,
                interaction.ReadPercentage))
            .SingleAsync(cancellationToken);

        return Result<ArticleInteractionResponse>.Success(stored);
    }
}
