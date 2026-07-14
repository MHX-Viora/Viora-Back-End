namespace Viora.Domain.Entities;

public sealed class Post : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid? OriginalPostId { get; set; }
    public string? Content { get; set; }
    public PostType PostType { get; set; }
    public PostVisibility Visibility { get; set; } = PostVisibility.Public;
    public PostStatus Status { get; set; } = PostStatus.Published;
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Link { get; set; }
    public int ReactionCount { get; set; }
    public int CommentCount { get; set; }
    public int ShareCount { get; set; }
    public int SaveCount { get; set; }
    public int ViewCount { get; set; }
    public DateTime? DeletedAt { get; set; }
    public User User { get; set; } = null!;
    public Post? OriginalPost { get; set; }
    public ICollection<Post> Shares { get; set; } = [];
    public ICollection<PostMedia> Media { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
}

public sealed class PostMedia : CreatedEntity
{
    public Guid PostId { get; set; }
    public string MediaUrl { get; set; } = null!;
    public string? ThumbnailUrl { get; set; }
    public Post Post { get; set; } = null!;
}

public sealed class PostReaction
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public ReactionType ReactionType { get; set; } = ReactionType.Like;
    public DateTime CreatedAt { get; set; }
    public Post Post { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class Comment : AuditableEntity
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public Guid? ReplyToUserId { get; set; }
    public string Content { get; set; } = null!;
    public int LikeCount { get; set; }
    public int ReplyCount { get; set; }
    public CommentStatus Status { get; set; } = CommentStatus.Published;
    public DateTime? DeletedAt { get; set; }
    public Post Post { get; set; } = null!;
    public User User { get; set; } = null!;
    public Comment? ParentComment { get; set; }
    public User? ReplyToUser { get; set; }
    public ICollection<Comment> Replies { get; set; } = [];
}

public sealed class CommentReaction
{
    public Guid CommentId { get; set; }
    public Guid UserId { get; set; }
    public ReactionType ReactionType { get; set; } = ReactionType.Like;
    public DateTime CreatedAt { get; set; }
    public Comment Comment { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class Hashtag : CreatedEntity
{
    public string Name { get; set; } = null!;
    public int PostCount { get; set; }
}

public sealed class PostHashtag
{
    public Guid PostId { get; set; }
    public Guid HashtagId { get; set; }
    public Post Post { get; set; } = null!;
    public Hashtag Hashtag { get; set; } = null!;
}

public sealed class SavedPost : CreatedEntity
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public User User { get; set; } = null!;
    public Post Post { get; set; } = null!;
}

public sealed class ViewHistory : Entity
{
    public Guid? UserId { get; set; }
    public Guid? PostId { get; set; }
    public int WatchDuration { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? ViewedAt { get; set; }
    public User? User { get; set; }
    public Post? Post { get; set; }
}
