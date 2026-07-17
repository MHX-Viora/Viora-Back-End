using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Realtime;

public static class RealtimeEvents
{
    public const string ReceiveNotification = nameof(ReceiveNotification);
    public const string ReceiveMessage = nameof(ReceiveMessage);
    public const string NewMessageNotification = nameof(NewMessageNotification);
    public const string MessageDelivered = nameof(MessageDelivered);
    public const string MessageUpdated = nameof(MessageUpdated);
    public const string MessageEdited = nameof(MessageEdited);
    public const string MessageDeleted = nameof(MessageDeleted);
    public const string ConversationRead = nameof(ConversationRead);
    public const string MessageRead = nameof(MessageRead);
    public const string MessagesRead = nameof(MessagesRead);
    public const string ConversationUpdated = nameof(ConversationUpdated);
    public const string ConversationCreated = nameof(ConversationCreated);
    public const string ReactionAdded = nameof(ReactionAdded);
    public const string ReactionRemoved = nameof(ReactionRemoved);
    public const string FriendRequestReceived = nameof(FriendRequestReceived);
    public const string FriendRequestAccepted = nameof(FriendRequestAccepted);
    public const string UserFollowed = nameof(UserFollowed);
    public const string TypingStarted = nameof(TypingStarted);
    public const string TypingStopped = nameof(TypingStopped);
    public const string UserOnline = nameof(UserOnline);
    public const string UserOffline = nameof(UserOffline);
    public const string ConversationPinned = nameof(ConversationPinned);
    public const string ConversationPinnedChanged = nameof(ConversationPinnedChanged);
    public const string ConversationMuted = nameof(ConversationMuted);
    public const string ConversationRenamed = nameof(ConversationRenamed);
    public const string ConversationAvatarChanged = nameof(ConversationAvatarChanged);
    public const string MemberAdded = nameof(MemberAdded);
    public const string MemberRemoved = nameof(MemberRemoved);
    public const string MemberLeft = nameof(MemberLeft);
}

public sealed record RegisterDeviceTokenCommand(
    Guid UserId,
    string Token,
    string? DeviceId,
    string? DeviceName,
    DevicePlatform Platform,
    string? AppVersion) : IRequest<DeviceTokenResponse>;

public sealed record UnregisterDeviceTokenCommand(Guid UserId, string Token)
    : IRequest<DeviceTokenResponse>;

public sealed record DeviceTokenResponse(bool Success, bool IsActive, string? Message = null);

public sealed record PushMessage(
    Guid UserId,
    string Title,
    string? Body,
    IReadOnlyDictionary<string, string> Data);

public interface IRealtimeService
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken);
    Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken);
    Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken);
}

public interface IPushNotificationSender
{
    Task SendAsync(PushMessage message, CancellationToken cancellationToken);
}

public interface IOnlineUserRegistry
{
    bool IsOnline(Guid userId);
}

public interface IDeviceTokenRepository
{
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken);
    Task<DeviceToken?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken);
    Task<DeviceToken?> GetByTokenOrDeviceIdAsync(string token, string? deviceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken);
    Task DeactivateAsync(string token, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class RegisterDeviceTokenValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    public RegisterDeviceTokenValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Token).NotEmpty().MaximumLength(4096);
        RuleFor(command => command.DeviceId).MaximumLength(255);
        RuleFor(command => command.DeviceName).MaximumLength(255);
        RuleFor(command => command.AppVersion).MaximumLength(50);
        RuleFor(command => command.Platform).IsInEnum();
    }
}

public sealed class UnregisterDeviceTokenValidator : AbstractValidator<UnregisterDeviceTokenCommand>
{
    public UnregisterDeviceTokenValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Token).NotEmpty().MaximumLength(4096);
    }
}
