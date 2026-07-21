using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.InviteCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ConversationType).IsRequired();
        builder.Property(x => x.CanSendMessage).HasDefaultValue(ConversationSendPermission.Everyone);
        builder.HasIndex(x => x.InviteCode).IsUnique();
        builder.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LastMessage).WithMany().HasForeignKey(x => x.LastMessageId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("ConversationMembers");
        builder.HasKey(x => new { x.ConversationId, x.UserId });
        builder.Property(x => x.Role).HasDefaultValue(ConversationMemberRole.Member);
        builder.Property(x => x.IsMuted).HasDefaultValue(false);
        builder.Property(x => x.IsPinned).HasDefaultValue(false);
        builder.HasOne(x => x.Conversation).WithMany(x => x.Members).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LastReadMessage).WithMany().HasForeignKey(x => x.LastReadMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Inviter).WithMany().HasForeignKey(x => x.JoinedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MessageType).IsRequired();
        builder.Property(x => x.IsEdited).HasDefaultValue(false);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SenderUser).WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReplyMessage).WithMany().HasForeignKey(x => x.ReplyMessageId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("MessageAttachments", table => table.HasCheckConstraint(
            "CK_MessageAttachments_NonNegativeMetadata",
            "(\"FileSize\" IS NULL OR \"FileSize\" >= 0) AND (\"Duration\" IS NULL OR \"Duration\" >= 0)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileUrl).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.MimeType).HasMaxLength(100);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
        builder.HasOne(x => x.Message).WithMany(x => x.Attachments).HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> builder)
    {
        builder.ToTable("MessageReactions");
        builder.HasKey(x => new { x.MessageId, x.UserId });
        builder.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageReadConfiguration : IEntityTypeConfiguration<MessageRead>
{
    public void Configure(EntityTypeBuilder<MessageRead> builder)
    {
        builder.ToTable("MessageReads");
        builder.HasKey(x => new { x.MessageId, x.UserId });
        builder.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConversationBlockConfiguration : IEntityTypeConfiguration<ConversationBlock>
{
    public void Configure(EntityTypeBuilder<ConversationBlock> builder)
    {
        builder.ToTable("ConversationBlocks");
        builder.HasKey(x => new { x.ConversationId, x.UserId });
        builder.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
