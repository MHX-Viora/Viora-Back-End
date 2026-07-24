namespace Viora.Domain.Entities;

public sealed class Conversation : AuditableEntity
{
    public ConversationType ConversationType { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string InviteCode { get; set; } = null!;
    public Guid? LastMessageId { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public ConversationSendPermission CanSendMessage { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public User? Creator { get; set; }
    public Message? LastMessage { get; set; }
    public ICollection<ConversationMember> Members { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<CallSession> CallSessions { get; set; } = [];
}

public sealed class ConversationMember
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public ConversationMemberRole Role { get; set; } = ConversationMemberRole.Member;
    public DateTime JoinedAt { get; set; }
    public Guid? LastReadMessageId { get; set; }
    public DateTime? LastReadAt { get; set; }
    public bool IsMuted { get; set; }
    public bool IsPinned { get; set; }
    public ConversationMemberStatus? Status { get; set; }
    public Guid? JoinedBy { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
    public Message? LastReadMessage { get; set; }
    public User? Inviter { get; set; }
}

public sealed class Message : AuditableEntity
{
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid? ReplyMessageId { get; set; }
    public MessageType MessageType { get; set; }
    public string? Content { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public User SenderUser { get; set; } = null!;
    public Message? ReplyMessage { get; set; }
    public ICollection<MessageAttachment> Attachments { get; set; } = [];
}

public sealed class MessageAttachment : Entity
{
    public Guid MessageId { get; set; }
    public string FileUrl { get; set; } = null!;
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long? FileSize { get; set; }
    public int? Duration { get; set; }
    public Message Message { get; set; } = null!;
}

public sealed class MessageReaction
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public ReactionType ReactionType { get; set; }
    public DateTime? CreatedAt { get; set; }
    public Message Message { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class MessageRead
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; }
    public Message Message { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class ConversationBlock
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class CallSession : AuditableEntity
{
    public Guid ConversationId { get; set; }
    public Guid CallerId { get; set; }
    public Guid ReceiverId { get; set; }
    public CallType CallType { get; set; } = CallType.Audio;
    public CallStatus Status { get; set; } = CallStatus.Calling;
    public DateTime StartedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? Duration { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public User Caller { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}
