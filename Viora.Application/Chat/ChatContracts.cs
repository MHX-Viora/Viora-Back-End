using MediatR;
using System.Text.Json;
using FluentValidation;
using Viora.Application.Realtime;
using Viora.Application.Posts;
using Viora.Domain.Entities;

namespace Viora.Application.Chat;

public sealed record GetChatConversationsQuery(
    Guid UserId,
    int Page,
    int PageSize,
    string? Keyword) : IRequest<ChatConversationListResponse>;

public sealed record GetChatConversationMessagesQuery(
    Guid UserId,
    Guid ConversationId,
    int Page,
    int PageSize) : IRequest<ChatResult<ChatMessageListResponse>>;

public sealed record SendChatMessageCommand(
    Guid SenderUserId,
    Guid ConversationId,
    Guid? ReplyMessageId,
    MessageType MessageType,
    string? Content,
    IReadOnlyList<SendChatMessageAttachmentRequest>? Attachments)
    : IRequest<ChatResult<SendChatMessageResponse>>;

public sealed record MarkConversationReadCommand(
    Guid UserId,
    Guid ConversationId) : IRequest<ChatResult<MarkConversationReadResponse>>;

public sealed record UploadChatAttachmentsCommand(
    Guid UserId,
    IReadOnlyList<CreatePostFile> Files) : IRequest<ChatResult<IReadOnlyList<ChatAttachmentUploadResponse>>>;

public sealed record RecallChatMessageCommand(
    Guid UserId,
    Guid MessageId) : IRequest<ChatResult<RecallChatMessageResponse>>;

public sealed record SendChatMessageAttachmentRequest(
    string FileUrl,
    string? FileName,
    string? MimeType,
    string? ThumbnailUrl,
    long? FileSize,
    int? Duration);

public sealed record ChatAttachmentUploadResponse(
    string FileUrl,
    string FileName,
    string? MimeType,
    string? ThumbnailUrl,
    int? Duration,
    long FileSize);

public enum ChatError
{
    ConversationNotFound,
    MessageNotFound,
    Forbidden,
    Validation
}

public sealed record ChatResult<T>(bool IsSuccess, T? Value, ChatError? Error, string? Message)
{
    public static ChatResult<T> Success(T value) => new(true, value, null, null);
    public static ChatResult<T> Failure(ChatError error, string message) => new(false, default, error, message);
}

public sealed record ChatConversationListResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<ChatConversationItemResponse> Items);

public sealed record ChatConversationItemResponse(
    Guid Id,
    ConversationType ConversationType,
    string? Name,
    string? AvatarUrl,
    ChatParticipantResponse? OtherParticipant,
    int MemberCount,
    ChatLastMessageResponse? LastMessage,
    int UnreadCount,
    bool IsMuted,
    bool IsPinned,
    DateTime? LastMessageAt);

public sealed record ChatLastMessageResponse(
    Guid Id,
    Guid SenderId,
    string SenderName,
    MessageType MessageType,
    string? Content,
    IReadOnlyList<ChatMessageAttachmentResponse> Attachments,
    DateTime CreatedAt,
    bool IsMine);

public sealed record ChatMessageListResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<ChatMessageItemResponse> Items);

public sealed record ChatMessageItemResponse(
    Guid Id,
    ChatMessageSenderResponse Sender,
    MessageType MessageType,
    string? Content,
    ChatReplyMessageResponse? ReplyMessage,
    IReadOnlyList<ChatMessageAttachmentResponse> Attachments,
    IReadOnlyList<ChatMessageReactionResponse> Reactions,
    ChatReactionSummaryResponse ReactionSummary,
    bool IsMine,
    bool IsEdited,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SendChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    ChatMessageSenderResponse Sender,
    MessageType MessageType,
    string? Content,
    ChatReplyMessageResponse? ReplyMessage,
    IReadOnlyList<ChatMessageAttachmentResponse> Attachments,
    bool IsMine,
    bool IsEdited,
    bool IsDeleted,
    DateTime CreatedAt);

public sealed record ChatRealtimeMessageResponse(
    Guid Id,
    Guid ConversationId,
    ChatMessageSenderResponse Sender,
    MessageType MessageType,
    string? Content,
    ChatReplyMessageResponse? Reply,
    IReadOnlyList<ChatMessageAttachmentResponse> Attachments,
    IReadOnlyList<ChatMessageReactionResponse> Reactions,
    bool IsMine,
    bool IsEdited,
    bool IsDeleted,
    DateTime CreatedAt);

public sealed record NewMessageNotificationPayload(
    Guid ConversationId,
    ConversationType ConversationType,
    string? ConversationName,
    string? ConversationAvatarUrl,
    ChatMessageSenderResponse Sender,
    NewMessageNotificationMessagePayload Message,
    int UnreadCount,
    bool IsMuted);

public sealed record NewMessageNotificationMessagePayload(
    Guid Id,
    string? Content,
    MessageType MessageType,
    IReadOnlyList<ChatMessageAttachmentResponse> Attachments,
    DateTime CreatedAt);

public sealed record MessageDeliveredPayload(
    Guid ConversationId,
    Guid MessageId,
    Guid UserId,
    DateTime DeliveredAt);

public sealed record MessageDeletedPayload(
    Guid ConversationId,
    Guid MessageId,
    Guid DeletedBy,
    DateTime DeletedAt);

public sealed record RecallChatMessageResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid DeletedBy,
    DateTime DeletedAt);

public sealed record MarkConversationReadResponse(
    Guid ConversationId,
    Guid? LastReadMessageId,
    DateTime ReadAt);

public sealed record MessagesReadRealtimePayload(
    Guid ConversationId,
    Guid UserId,
    Guid? LastReadMessageId,
    DateTime ReadAt,
    int UnreadCount);

public sealed record ChatMessageSenderResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record ChatParticipantResponse(
    Guid Id,
    string DisplayName,
    string? AvatarUrl);

public sealed record ChatReplyMessageResponse(
    Guid Id,
    string? Content,
    MessageType MessageType,
    string SenderName);

public sealed record ChatMessageAttachmentResponse(
    Guid Id,
    string FileUrl,
    string? FileName,
    string? MimeType,
    string? ThumbnailUrl,
    long? FileSize,
    int? Duration);

public sealed record ChatMessageReactionResponse(
    Guid UserId,
    string DisplayName,
    ReactionType ReactionType);

public sealed record ChatReactionSummaryResponse(
    int Like,
    int Love,
    int Haha,
    int Wow,
    int Sad,
    int Angry,
    int Total);

public interface IChatConversationRepository
{
    Task<ChatConversationListResponse> GetConversationsAsync(
        GetChatConversationsQuery query,
        CancellationToken cancellationToken);

    Task<ChatResult<ChatMessageListResponse>> GetMessagesAsync(
        GetChatConversationMessagesQuery query,
        CancellationToken cancellationToken);

    Task<ChatResult<SendChatMessageRepositoryResult>> SendMessageAsync(
        SendChatMessageCommand command,
        CancellationToken cancellationToken);

    Task<ChatResult<MarkConversationReadRepositoryResult>> MarkReadAsync(
        MarkConversationReadCommand command,
        CancellationToken cancellationToken);

    Task<ChatConversationItemResponse?> GetConversationItemAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<ChatResult<RecallChatMessageRepositoryResult>> RecallMessageAsync(
        RecallChatMessageCommand command,
        CancellationToken cancellationToken);
}

public sealed record SendChatMessageRepositoryResult(
    SendChatMessageResponse Message,
    IReadOnlyList<ChatConversationRecipientState> Recipients);

public sealed record MarkConversationReadRepositoryResult(
    MarkConversationReadResponse Response,
    IReadOnlyList<Guid> ConversationMemberIds,
    bool DidUpdate);

public sealed record ChatConversationRecipientState(
    Guid UserId,
    bool IsMuted,
    int UnreadCount);

public sealed record RecallChatMessageRepositoryResult(
    RecallChatMessageResponse Response,
    IReadOnlyList<Guid> ConversationMemberIds);

public sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator()
    {
        RuleFor(command => command.SenderUserId).NotEmpty();
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.MessageType)
            .Must(type => type != MessageType.Recall && Enum.IsDefined(type))
            .WithMessage("Loai tin nhan khong hop le.");
        RuleFor(command => command.Attachments)
            .Must(attachments => attachments is null || attachments.Count <= 10)
            .WithMessage("Toi da 10 tep dinh kem.");
        RuleForEach(command => command.Attachments).ChildRules(attachment =>
        {
            attachment.RuleFor(value => value.FileUrl)
                .NotEmpty()
                .MaximumLength(2048)
                .Must(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                .WithMessage("FileUrl phai la URL public HTTPS.");
            attachment.RuleFor(value => value.FileName).MaximumLength(255);
            attachment.RuleFor(value => value.MimeType).MaximumLength(100);
            attachment.RuleFor(value => value.ThumbnailUrl).MaximumLength(2048);
            attachment.RuleFor(value => value.FileSize).GreaterThanOrEqualTo(0).When(value => value.FileSize.HasValue);
            attachment.RuleFor(value => value.Duration).GreaterThanOrEqualTo(0).When(value => value.Duration.HasValue);
        });
        RuleFor(command => command).Custom((command, context) =>
        {
            var attachments = command.Attachments ?? [];
            var content = command.Content?.Trim();

            switch (command.MessageType)
            {
                case MessageType.Text:
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        context.AddFailure(nameof(command.Content), "Noi dung tin nhan la bat buoc.");
                    }
                    if (attachments.Count > 0)
                    {
                        context.AddFailure(nameof(command.Attachments), "Tin nhan text khong duoc co tep dinh kem.");
                    }
                    break;
                case MessageType.Image:
                case MessageType.Video:
                case MessageType.File:
                    if (attachments.Count == 0)
                    {
                        context.AddFailure(nameof(command.Attachments), "Tin nhan can it nhat mot tep dinh kem.");
                    }
                    break;
                case MessageType.Audio:
                    if (attachments.Count == 0)
                    {
                        context.AddFailure(nameof(command.Attachments), "Tin nhan audio can it nhat mot tep dinh kem.");
                    }
                    if (attachments.Any(attachment => !attachment.Duration.HasValue))
                    {
                        context.AddFailure(nameof(command.Attachments), "Tin nhan audio can duration.");
                    }
                    break;
                case MessageType.Sticker:
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        context.AddFailure(nameof(command.Content), "StickerId hoac StickerCode la bat buoc.");
                    }
                    if (attachments.Count > 0)
                    {
                        context.AddFailure(nameof(command.Attachments), "Tin nhan sticker khong duoc co tep dinh kem.");
                    }
                    break;
                case MessageType.Location:
                    if (!IsValidLocationJson(content))
                    {
                        context.AddFailure(nameof(command.Content), "Noi dung location phai la JSON hop le.");
                    }
                    if (attachments.Count > 0)
                    {
                        context.AddFailure(nameof(command.Attachments), "Tin nhan location khong duoc co tep dinh kem.");
                    }
                    break;
            }
        });
    }

    private static bool IsValidLocationJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("latitude", out var latitude) &&
                   root.TryGetProperty("longitude", out var longitude) &&
                   latitude.TryGetDouble(out _) &&
                   longitude.TryGetDouble(out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
