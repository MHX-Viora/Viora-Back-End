using Microsoft.EntityFrameworkCore;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private const string InviteCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Random SharedRandom = Random.Shared;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<PostReaction> PostReactions => Set<PostReaction>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentReaction> CommentReactions => Set<CommentReaction>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Hashtag> Hashtags => Set<Hashtag>();
    public DbSet<PostHashtag> PostHashtags => Set<PostHashtag>();
    public DbSet<SavedPost> SavedPosts => Set<SavedPost>();
    public DbSet<ViewHistory> ViewHistories => Set<ViewHistory>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<MessageRead> MessageReads => Set<MessageRead>();
    public DbSet<ConversationBlock> ConversationBlocks => Set<ConversationBlock>();
    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AdminLog> AdminLogs => Set<AdminLog>();

    [DbFunction("translate", IsBuiltIn = true)]
    public static string Translate(string value, string matching, string replacement) =>
        throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(DateTime))
            {
                property.SetColumnType("timestamp with time zone");
            }
            else if (type.IsEnum)
            {
                property.SetColumnType("smallint");
            }
        }

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
        {
            if (foreignKey.DeleteBehavior == DeleteBehavior.ClientSetNull)
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUtcTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUtcTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public async Task<string> CreateUniqueInviteCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = CreateInviteCode();
            if (!await Conversations.AnyAsync(conversation => conversation.InviteCode == code, cancellationToken))
            {
                return code;
            }
        }

        return Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    }

    private void StampUtcTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAt") is not null)
            {
                entry.Property("CreatedAt").CurrentValue = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified && entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }

            if (entry.State == EntityState.Added &&
                entry.Entity is Conversation conversation &&
                string.IsNullOrWhiteSpace(conversation.InviteCode))
            {
                conversation.InviteCode = CreateInviteCode();
            }
        }
    }

    private static string CreateInviteCode()
    {
        Span<char> chars = stackalloc char[6];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = InviteCodeAlphabet[SharedRandom.Next(InviteCodeAlphabet.Length)];
        }

        return new string(chars);
    }
}
