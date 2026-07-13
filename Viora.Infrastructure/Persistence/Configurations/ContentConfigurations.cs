using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("Posts", table => table.HasCheckConstraint(
            "CK_Posts_NonNegativeCounts",
            "\"ReactionCount\" >= 0 AND \"CommentCount\" >= 0 AND \"ShareCount\" >= 0 AND \"SaveCount\" >= 0 AND \"ViewCount\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PostType).IsRequired();
        builder.Property(x => x.Visibility).HasDefaultValue(PostVisibility.Public);
        builder.Property(x => x.Status).HasDefaultValue(PostStatus.Published).HasSentinel(PostStatus.Published);
        builder.Property(x => x.Location).HasMaxLength(255);
        builder.Property(x => x.Link).HasMaxLength(255);
        builder.Property(x => x.ReactionCount).HasDefaultValue(0);
        builder.Property(x => x.CommentCount).HasDefaultValue(0);
        builder.Property(x => x.ShareCount).HasDefaultValue(0);
        builder.Property(x => x.SaveCount).HasDefaultValue(0);
        builder.Property(x => x.ViewCount).HasDefaultValue(0);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginalPost).WithMany(x => x.Shares).HasForeignKey(x => x.OriginalPostId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PostMediaConfiguration : IEntityTypeConfiguration<PostMedia>
{
    public void Configure(EntityTypeBuilder<PostMedia> builder)
    {
        builder.ToTable("PostMedia");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MediaUrl).IsRequired();
        builder.HasOne(x => x.Post).WithMany(x => x.Media).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PostReactionConfiguration : IEntityTypeConfiguration<PostReaction>
{
    public void Configure(EntityTypeBuilder<PostReaction> builder)
    {
        builder.ToTable("PostReactions");
        builder.HasKey(x => new { x.PostId, x.UserId });
        builder.Property(x => x.ReactionType).HasDefaultValue(ReactionType.Like);
        builder.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments", table => table.HasCheckConstraint(
            "CK_Comments_NonNegativeCounts", "\"LikeCount\" >= 0 AND \"ReplyCount\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.LikeCount).HasDefaultValue(0);
        builder.Property(x => x.ReplyCount).HasDefaultValue(0);
        builder.Property(x => x.Status).HasDefaultValue(CommentStatus.Published).HasSentinel(CommentStatus.Published);
        builder.HasOne(x => x.Post).WithMany(x => x.Comments).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentComment).WithMany(x => x.Replies).HasForeignKey(x => x.ParentCommentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReplyToUser).WithMany().HasForeignKey(x => x.ReplyToUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommentReactionConfiguration : IEntityTypeConfiguration<CommentReaction>
{
    public void Configure(EntityTypeBuilder<CommentReaction> builder)
    {
        builder.ToTable("CommentReactions");
        builder.HasKey(x => new { x.CommentId, x.UserId });
        builder.Property(x => x.ReactionType).HasDefaultValue(ReactionType.Like);
        builder.HasOne(x => x.Comment).WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HashtagConfiguration : IEntityTypeConfiguration<Hashtag>
{
    public void Configure(EntityTypeBuilder<Hashtag> builder)
    {
        builder.ToTable("Hashtags", table => table.HasCheckConstraint("CK_Hashtags_PostCount", "\"PostCount\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PostCount).HasDefaultValue(0);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class PostHashtagConfiguration : IEntityTypeConfiguration<PostHashtag>
{
    public void Configure(EntityTypeBuilder<PostHashtag> builder)
    {
        builder.ToTable("PostHashtags");
        builder.HasKey(x => new { x.PostId, x.HashtagId });
        builder.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Hashtag).WithMany().HasForeignKey(x => x.HashtagId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SavedPostConfiguration : IEntityTypeConfiguration<SavedPost>
{
    public void Configure(EntityTypeBuilder<SavedPost> builder)
    {
        builder.ToTable("SavedPosts");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.PostId }).IsUnique();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ViewHistoryConfiguration : IEntityTypeConfiguration<ViewHistory>
{
    public void Configure(EntityTypeBuilder<ViewHistory> builder)
    {
        builder.ToTable("ViewHistories", table => table.HasCheckConstraint("CK_ViewHistories_WatchDuration", "\"WatchDuration\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.WatchDuration).HasDefaultValue(0);
        builder.Property(x => x.IsCompleted).HasDefaultValue(false);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Restrict);
    }
}
